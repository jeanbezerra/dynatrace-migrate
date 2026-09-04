namespace A2D.AlertMigrator.Application.Remote;

public enum CertificateValidationMode
{
    SystemTrust,
    CustomCertificateAuthority,
    Sha256Pinning,
    DangerousAcceptAny
}
