using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;
using A2D.AlertMigrator.Infrastructure.Importing.Json.Contracts;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Parsing;

internal sealed partial class CanonicalJsonV1Parser : IJsonDocumentParser
{
    private static readonly HashSet<string> SelectorTypes = ["ENTITY_ID", "NAME"];
    private static readonly HashSet<string> Aggregations = ["AVG", "SUM", "MIN", "MAX", "COUNT"];
    private static readonly HashSet<string> Rollups = ["AVG", "MIN", "MAX", "SUM", "TOTAL"];
    private static readonly HashSet<string> Models = ["STATIC", "AUTO_ADAPTIVE", "SEASONAL"];
    private static readonly HashSet<string> Conditions = ["ABOVE", "BELOW", "OUTSIDE"];
    private static readonly HashSet<string> EventTypes =
    [
        "AVAILABILITY_EVENT",
        "ERROR_EVENT",
        "PERFORMANCE_EVENT",
        "RESOURCE_CONTENTION_EVENT",
        "CUSTOM_ALERT",
        "CUSTOM_INFO"
    ];

    private static readonly HashSet<string> ProfileSeverities =
    [
        "AVAILABILITY",
        "ERROR",
        "PERFORMANCE",
        "RESOURCE_CONTENTION",
        "CUSTOM_ALERT",
        "INFO"
    ];

    private static readonly HashSet<string> ScheduleModes = ["ALWAYS", "ACTIVE_WINDOW", "NOTIFICATION_ONLY"];
    private static readonly HashSet<string> WeekDays = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];

    public ParseResult Parse(ReadOnlySpan<byte> utf8Json, string relativePath, ImportLimits limits)
    {
        var diagnostics = new List<ImportDiagnostic>();

        try
        {
            foreach (var duplicate in JsonDuplicatePropertyScanner.Scan(utf8Json, limits.MaxJsonDepth))
            {
                var location = JsonDuplicatePropertyScanner.Locate(utf8Json, duplicate.ByteOffset);
                diagnostics.Add(Error(
                    "JSON_DUPLICATE_PROPERTY",
                    $"Propriedade '{duplicate.Name}' repetida na linha {location.Line + 1}, byte {location.ByteInLine + 1}.",
                    relativePath,
                    byteOffset: duplicate.ByteOffset));
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add(JsonError("JSON_SYNTAX_ERROR", exception, relativePath));
            return new ParseResult(null, diagnostics);
        }

        if (diagnostics.Count != 0)
        {
            return new ParseResult(null, diagnostics);
        }

        ApplicationFileDto? source;
        try
        {
            source = JsonSerializer.Deserialize<ApplicationFileDto>(utf8Json, CreateSerializerOptions(limits.MaxJsonDepth));
        }
        catch (JsonException exception)
        {
            var code = exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase)
                ? "JSON_SCHEMA_VIOLATION"
                : "JSON_SYNTAX_ERROR";
            diagnostics.Add(JsonError(code, exception, relativePath));
            return new ParseResult(null, diagnostics);
        }

        if (source is null)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "O documento JSON não pode ser nulo.", relativePath));
            return new ParseResult(null, diagnostics);
        }

        if (!string.Equals(source.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "JSON_SCHEMA_UNSUPPORTED",
                $"Versão de schema não suportada: '{source.SchemaVersion ?? "<ausente>"}'.",
                relativePath,
                "/schemaVersion"));
            return new ParseResult(null, diagnostics);
        }

        var application = BuildApplication(source.Application, diagnostics, relativePath);
        var rules = BuildRules(source, application?.Id, diagnostics, relativePath, limits);

        if (application is null)
        {
            return new ParseResult(null, diagnostics);
        }

        ValidateGroups(rules, diagnostics, relativePath, application.Id);

        return new ParseResult(
            new ApplicationImportDocument("1.0", application, rules),
            diagnostics);
    }

    private static ApplicationIdentity? BuildApplication(
        ApplicationDto? source,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath)
    {
        if (source is null)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Objeto 'application' é obrigatório.", relativePath, "/application"));
            return null;
        }

        var id = RequiredText(source.Id, "application.id", 128, diagnostics, relativePath, "/application/id");
        var name = RequiredText(source.Name, "application.name", 256, diagnostics, relativePath, "/application/name");

        if (id is not null && !IdentifierRegex().IsMatch(id))
        {
            diagnostics.Add(Error(
                "JSON_SCHEMA_VIOLATION",
                "application.id deve começar com letra ou número e conter somente letras, números, ponto, hífen ou sublinhado.",
                relativePath,
                "/application/id",
                applicationId: id));
        }

        if (source.Description?.Length > 4096)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "application.description excede 4096 caracteres.", relativePath, "/application/description", id));
        }

        var owners = (source.Owners ?? []).Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (owners.Length > 50 || owners.Distinct(StringComparer.Ordinal).Count() != owners.Length)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "application.owners deve ter até 50 valores únicos.", relativePath, "/application/owners", id));
        }

        var labels = source.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (labels.Count > 100 || labels.Any(static pair => pair.Key.Length is 0 or > 128 || pair.Value.Length > 1024))
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "application.labels excede os limites permitidos.", relativePath, "/application/labels", id));
        }

        return id is null || name is null
            ? null
            : new ApplicationIdentity(id, name, source.Description, owners, labels);
    }

    private static IReadOnlyList<CanonicalAlertRule> BuildRules(
        ApplicationFileDto source,
        string? applicationId,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        ImportLimits limits)
    {
        if (source.Rules is null || source.Rules.Count == 0)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "A aplicação deve conter ao menos uma regra.", relativePath, "/rules", applicationId));
            return [];
        }

        if (source.Rules.Count > limits.MaxRulesPerApplication)
        {
            diagnostics.Add(Error(
                "JSON_RULE_LIMIT_EXCEEDED",
                $"A aplicação possui {source.Rules.Count} regras; o limite é {limits.MaxRulesPerApplication}.",
                relativePath,
                "/rules",
                applicationId));
            return [];
        }

        var rules = new List<CanonicalAlertRule>(source.Rules.Count);
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < source.Rules.Count; index++)
        {
            var pointer = $"/rules/{index}";
            var rule = BuildRule(source.Rules[index], source.Defaults, diagnostics, relativePath, pointer, applicationId, limits);
            if (rule is null)
            {
                continue;
            }

            if (!ruleIds.Add(rule.Id))
            {
                diagnostics.Add(Error(
                    "RULE_ID_DUPLICATE",
                    $"ID de regra repetido na aplicação: '{rule.Id}'.",
                    relativePath,
                    $"{pointer}/id",
                    applicationId,
                    rule.Id));
            }

            rules.Add(rule);
        }

        return rules;
    }

    private static CanonicalAlertRule? BuildRule(
        RuleDto source,
        DefaultsDto? defaults,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        ImportLimits limits)
    {
        var errorCountBefore = diagnostics.Count(static diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Error);
        var id = RequiredText(source.Id, "rule.id", 128, diagnostics, relativePath, $"{pointer}/id", applicationId);
        var name = RequiredText(source.Name, "rule.name", 256, diagnostics, relativePath, $"{pointer}/name", applicationId, id);

        if (id is not null && !IdentifierRegex().IsMatch(id))
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "rule.id possui formato inválido.", relativePath, $"{pointer}/id", applicationId, id));
        }

        if (source.GroupId is not null && !IdentifierRegex().IsMatch(source.GroupId))
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "rule.groupId possui formato inválido.", relativePath, $"{pointer}/groupId", applicationId, id));
        }

        if (source.GroupId?.Length > 128)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "rule.groupId excede 128 caracteres.", relativePath, $"{pointer}/groupId", applicationId, id));
        }

        if (source.Description?.Length > 4096)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "rule.description excede 4096 caracteres.", relativePath, $"{pointer}/description", applicationId, id));
        }

        var targets = BuildTargets(source.Targets, diagnostics, relativePath, $"{pointer}/targets", applicationId, id);
        var signal = BuildSignal(source.Signal, diagnostics, relativePath, $"{pointer}/signal", applicationId, id, limits);
        var detector = BuildDetector(source.Detector, diagnostics, relativePath, $"{pointer}/detector", applicationId, id);
        var eventDefinition = BuildEvent(source.Event, defaults, diagnostics, relativePath, $"{pointer}/event", applicationId, id);
        var profile = BuildProfile(source.Profile ?? defaults?.Profile, diagnostics, relativePath, $"{pointer}/profile", applicationId, id);
        var schedule = BuildSchedule(source.Schedule ?? defaults?.Schedule, diagnostics, relativePath, $"{pointer}/schedule", applicationId, id);

        if (schedule.Mode == "ACTIVE_WINDOW" && detector?.AlertOnMissingData == true)
        {
            diagnostics.Add(Error(
                "SCHEDULE_MISSING_DATA_CONFLICT",
                "ACTIVE_WINDOW não pode ser combinado com alertOnMissingData=true.",
                relativePath,
                $"{pointer}/detector/alertOnMissingData",
                applicationId,
                id));
        }

        var errorCountAfter = diagnostics.Count(static diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Error);
        if (errorCountAfter != errorCountBefore
            || id is null
            || name is null
            || targets.Count == 0
            || signal is null
            || detector is null
            || eventDefinition is null)
        {
            return null;
        }

        return new CanonicalAlertRule(
            id,
            name,
            source.GroupId,
            source.Enabled ?? defaults?.Enabled ?? true,
            source.Description,
            targets,
            signal,
            detector,
            eventDefinition,
            profile,
            schedule);
    }

    private static IReadOnlyList<ServiceTarget> BuildTargets(
        List<TargetDto>? source,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        string? ruleId)
    {
        if (source is null || source.Count == 0)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "A regra deve possuir ao menos um serviço alvo.", relativePath, pointer, applicationId, ruleId));
            return [];
        }


        if (source.Count > 1000)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "A regra não pode exceder 1000 serviços alvo.", relativePath, pointer, applicationId, ruleId));
            return [];
        }

        var targets = new List<ServiceTarget>(source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var selector = NormalizeEnum(source[index].SelectorType);
            var value = source[index].Value?.Trim();
            if (selector is null || !SelectorTypes.Contains(selector) || string.IsNullOrWhiteSpace(value) || value.Length > 512)
            {
                diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Alvo requer selectorType ENTITY_ID/NAME e value não vazio.", relativePath, $"{pointer}/{index}", applicationId, ruleId));
                continue;
            }

            if (selector == "ENTITY_ID" && !value.StartsWith("SERVICE-", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Um ENTITY_ID de serviço deve começar com SERVICE-.", relativePath, $"{pointer}/{index}/value", applicationId, ruleId));
                continue;
            }

            targets.Add(new ServiceTarget(selector, value));
        }

        if (targets.Distinct().Count() != targets.Count)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "A lista de serviços contém alvos duplicados.", relativePath, pointer, applicationId, ruleId));
        }

        return targets;
    }

    private static SignalDefinition? BuildSignal(
        SignalDto? source,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        string? ruleId,
        ImportLimits limits)
    {
        var kind = NormalizeEnum(source?.Kind);
        switch (kind)
        {
            case "METRIC":
                {
                    var metricKey = source?.MetricKey?.Trim();
                    var aggregation = NormalizeEnum(source?.Aggregation);
                    var rollup = NormalizeEnum(source?.Rollup);
                    if (string.IsNullOrWhiteSpace(metricKey)
                        || metricKey.Length > 512
                        || metricKey.Any(char.IsWhiteSpace)
                        || aggregation is null
                        || !Aggregations.Contains(aggregation)
                        || (rollup is not null && !Rollups.Contains(rollup))
                        || source?.Expression is not null)
                    {
                        diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Sinal METRIC inválido.", relativePath, pointer, applicationId, ruleId));
                        return null;
                    }

                    return new MetricSignalDefinition(metricKey, aggregation, rollup);
                }
            case "DQL":
                {
                    var expression = source?.Expression?.Trim();
                    if (string.IsNullOrWhiteSpace(expression))
                    {
                        diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Sinal DQL requer expression.", relativePath, $"{pointer}/expression", applicationId, ruleId));
                        return null;
                    }

                    if (expression.Length > limits.MaxDqlCharacters)
                    {
                        diagnostics.Add(Error("DQL_TOO_LARGE", $"DQL excede {limits.MaxDqlCharacters} caracteres.", relativePath, $"{pointer}/expression", applicationId, ruleId));
                        return null;
                    }

                    if (source?.MetricKey is not null || source?.Aggregation is not null || source?.Rollup is not null)
                    {
                        diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Sinal DQL não aceita campos de METRIC.", relativePath, pointer, applicationId, ruleId));
                        return null;
                    }

                    return new DqlSignalDefinition(expression);
                }
            default:
                diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "signal.kind deve ser METRIC ou DQL.", relativePath, $"{pointer}/kind", applicationId, ruleId));
                return null;
        }
    }

    private static DetectorDefinition? BuildDetector(
        DetectorDto? source,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        string? ruleId)
    {
        var model = NormalizeEnum(source?.Model);
        var condition = NormalizeEnum(source?.Condition);
        if (source is null
            || model is null
            || !Models.Contains(model)
            || condition is null
            || !Conditions.Contains(condition)
            || source.ViolatingSamples is null or < 1 or > 60
            || source.SlidingWindow is null or < 1 or > 60
            || source.DealertingSamples is null or < 1 or > 60
            || source.AlertOnMissingData is null)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Configuração obrigatória do detector é inválida.", relativePath, pointer, applicationId, ruleId));
            return null;
        }

        if (source.ViolatingSamples > source.SlidingWindow)
        {
            diagnostics.Add(Error("DETECTOR_WINDOW_INVALID", "violatingSamples não pode exceder slidingWindow.", relativePath, pointer, applicationId, ruleId));
        }

        if (model == "STATIC" && (source.Threshold is null || condition == "OUTSIDE"))
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Detector STATIC requer threshold e aceita somente ABOVE/BELOW.", relativePath, pointer, applicationId, ruleId));
        }

        return new DetectorDefinition(
            model,
            condition,
            source.Threshold,
            source.NumberOfSignalFluctuations ?? (model == "AUTO_ADAPTIVE" ? 1 : null),
            source.Tolerance ?? (model == "SEASONAL" ? 4 : null),
            source.ViolatingSamples.GetValueOrDefault(),
            source.SlidingWindow.GetValueOrDefault(),
            source.DealertingSamples.GetValueOrDefault(),
            source.AlertOnMissingData.GetValueOrDefault());
    }

    private static EventDefinition? BuildEvent(
        EventDto? source,
        DefaultsDto? defaults,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        string? ruleId)
    {
        var name = RequiredText(source?.Name, "event.name", 512, diagnostics, relativePath, $"{pointer}/name", applicationId, ruleId);
        var type = NormalizeEnum(source?.Type ?? defaults?.EventType);
        if (type is null || !EventTypes.Contains(type))
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "event.type deve ser definido na regra ou nos defaults.", relativePath, $"{pointer}/type", applicationId, ruleId));
        }

        var alertGroup = source?.AlertGroup?.Trim() ?? defaults?.AlertGroup?.Trim();
        if (string.IsNullOrWhiteSpace(alertGroup))
        {
            diagnostics.Add(new ImportDiagnostic(
                "ALERT_GROUP_MISSING",
                ImportDiagnosticSeverity.Warning,
                "A regra não possui alertGroup; o roteamento por Workflow ficará menos previsível.",
                relativePath,
                $"{pointer}/alertGroup",
                applicationId,
                ruleId));
            alertGroup = null;
        }
        else if (alertGroup.Length > 256)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "event.alertGroup excede 256 caracteres.", relativePath, $"{pointer}/alertGroup", applicationId, ruleId));
        }

        if (source?.Description?.Length > 8192)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "event.description excede 8192 caracteres.", relativePath, $"{pointer}/description", applicationId, ruleId));
        }

        return name is null || type is null || !EventTypes.Contains(type)
            ? null
            : new EventDefinition(name, source?.Description, type, alertGroup);
    }

    private static ProfileDefinition? BuildProfile(
        ProfileDto? source,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        string? ruleId)
    {
        if (source is null)
        {
            return null;
        }

        var severity = NormalizeEnum(source.Severity);
        if (string.IsNullOrWhiteSpace(source.Name)
            || source.Name.Length > 256
            || severity is null
            || !ProfileSeverities.Contains(severity)
            || source.DelayMinutes is null or < 0 or > 1440)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Alerting profile inválido.", relativePath, pointer, applicationId, ruleId));
            return null;
        }

        var tags = source.TagFilters ?? [];
        if (tags.Count > 100
            || tags.Any(static tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 256)
            || tags.Distinct(StringComparer.Ordinal).Count() != tags.Count)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "profile.tagFilters deve conter até 100 valores únicos não vazios.", relativePath, $"{pointer}/tagFilters", applicationId, ruleId));
            return null;
        }

        return new ProfileDefinition(source.Name.Trim(), severity, source.DelayMinutes.GetValueOrDefault(), tags);
    }

    private static ScheduleDefinition BuildSchedule(
        ScheduleDto? source,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId,
        string? ruleId)
    {
        if (source is null)
        {
            return new ScheduleDefinition("ALWAYS", null, []);
        }

        var mode = NormalizeEnum(source.Mode);
        if (mode is null || !ScheduleModes.Contains(mode))
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "schedule.mode inválido.", relativePath, $"{pointer}/mode", applicationId, ruleId));
            return new ScheduleDefinition("ALWAYS", null, []);
        }

        if (mode == "ALWAYS")
        {
            return new ScheduleDefinition(mode, null, []);
        }

        if (string.IsNullOrWhiteSpace(source.Timezone)
            || source.Timezone.Length > 128
            || !TimeZoneInfo.TryFindSystemTimeZoneById(source.Timezone, out _)
            || source.Windows is null
            || source.Windows.Count is 0 or > 50)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Janela requer timezone IANA válido e ao menos um período.", relativePath, pointer, applicationId, ruleId));
            return new ScheduleDefinition(mode, source.Timezone, []);
        }

        var windows = new List<ScheduleWindow>(source.Windows.Count);
        for (var index = 0; index < source.Windows.Count; index++)
        {
            var window = source.Windows[index];
            var days = window.Days?.Select(NormalizeEnum).Where(static day => day is not null).Cast<string>().ToArray() ?? [];
            var validDays = days.Length > 0
                && days.All(WeekDays.Contains)
                && days.Distinct(StringComparer.Ordinal).Count() == days.Length;
            var validStart = TimeOnly.TryParseExact(window.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start);
            var validEnd = TimeOnly.TryParseExact(window.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end);
            if (!validDays || !validStart || !validEnd || start == end)
            {
                diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", "Período de horário inválido.", relativePath, $"{pointer}/windows/{index}", applicationId, ruleId));
                continue;
            }

            windows.Add(new ScheduleWindow(days, start, end));
        }

        return new ScheduleDefinition(mode, source.Timezone, windows);
    }

    private static void ValidateGroups(
        IReadOnlyList<CanonicalAlertRule> rules,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string applicationId)
    {
        foreach (var group in rules.Where(static rule => rule.GroupId is not null).GroupBy(static rule => rule.GroupId!, StringComparer.Ordinal))
        {
            var signatures = group
                .Select(static rule => JsonSerializer.Serialize(rule with
                {
                    Id = string.Empty,
                    Name = string.Empty,
                    GroupId = string.Empty,
                    Targets = []
                }))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count();

            if (signatures <= 1)
            {
                continue;
            }

            foreach (var rule in group)
            {
                diagnostics.Add(Error(
                    "GROUP_SEMANTIC_MISMATCH",
                    $"Regras do groupId '{group.Key}' diferem além de id, name e targets.",
                    relativePath,
                    applicationId: applicationId,
                    ruleId: rule.Id));
            }
        }
    }

    private static string? RequiredText(
        string? value,
        string field,
        int maxLength,
        ICollection<ImportDiagnostic> diagnostics,
        string relativePath,
        string pointer,
        string? applicationId = null,
        string? ruleId = null)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            diagnostics.Add(Error("JSON_SCHEMA_VIOLATION", $"{field} é obrigatório e deve ter até {maxLength} caracteres.", relativePath, pointer, applicationId, ruleId));
            return null;
        }

        return normalized;
    }

    private static string? NormalizeEnum(string? value) => value?.Trim().ToUpperInvariant();

    private static JsonSerializerOptions CreateSerializerOptions(int maxDepth) => new()
    {
        AllowTrailingCommas = false,
        MaxDepth = maxDepth,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static ImportDiagnostic JsonError(string code, JsonException exception, string relativePath) =>
        Error(
            code,
            $"{exception.Message} Linha {(exception.LineNumber ?? 0) + 1}, byte {(exception.BytePositionInLine ?? 0) + 1}.",
            relativePath,
            exception.Path);

    private static ImportDiagnostic Error(
        string code,
        string message,
        string relativePath,
        string? jsonPointer = null,
        string? applicationId = null,
        string? ruleId = null,
        long? byteOffset = null) =>
        new(code, ImportDiagnosticSeverity.Error, message, relativePath, jsonPointer, applicationId, ruleId, byteOffset);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}

internal sealed record ParseResult(
    ApplicationImportDocument? Document,
    IReadOnlyList<ImportDiagnostic> Diagnostics);
