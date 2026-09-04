# Assinatura e liberação no Windows

O aplicativo deve ser distribuído como o executável único gerado em
`artifacts/A2D.AlertMigrator-win-x64`. O script de execução já usa esse formato.

## Publicação corporativa

1. Solicite à equipe de segurança um certificado **Code Signing** com chave
   privada. O emissor ou o publicador também deve estar autorizado na política
   App Control da empresa.
2. Instale o [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/)
   para disponibilizar o SignTool.
3. Importe o certificado no repositório pessoal do usuário ou da máquina.
4. Publique e exija a assinatura:

```powershell
./scripts/publish-win-x64.ps1 `
  -CertificateThumbprint 'THUMBPRINT_FORNECIDO_PELA_EMPRESA' `
  -CertificateStore CurrentUser `
  -TimestampUrl 'https://servidor-rfc3161-da-empresa' `
  -RequireSignature
```

O script usa SHA-256, valida a finalidade do certificado, assina o executável e
interrompe a publicação se o Windows não validar a assinatura. A senha ou a
chave privada nunca deve ser salva no repositório.

## Diagnóstico desta máquina

O log `Microsoft-Windows-CodeIntegrity/Operational` registrou os eventos 3077 e
3033 para `A2D.AlertMigrator.dll`. A política ativa exige nível de assinatura
empresarial. Não havia marca de arquivo baixado e o binário estava sem
assinatura. Portanto, desativar o SmartScreen ou apenas renomear o arquivo não
resolve a causa.

Se um pacote assinado continuar bloqueado, envie à equipe de segurança o evento
3077 e o certificado publicador. Ela precisa incluir esse publicador na política
App Control ou liberar o artefato pelo mecanismo corporativo adotado.

Referências: [eventos do App Control](https://learn.microsoft.com/windows/security/application-security/application-control/windows-defender-application-control/operations/event-id-explanations)
e [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool).
