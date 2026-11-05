# LlamaloIA Backend (.NET 9)

API en ASP.NET Core para LlamaloAI: autenticación con Firebase, Firestore, PostgreSQL (EF Core) y servicios de voz e IA.

## Configuración de secretos

Las claves **no** se guardan en el repositorio. `appsettings.json` solo contiene valores vacíos.

En local, usa User Secrets:

```bash
cd ProyectoLlamaloIA.Backend
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "<clave>"
dotnet user-secrets set "ElevenLabs:ApiKey" "<clave>"
dotnet user-secrets set "Deepgram:ApiKey" "<clave>"
dotnet user-secrets set "Firebase:ApiKey" "<clave web de Firebase>"
dotnet user-secrets set "Firebase:CredentialsFile" "/ruta/a/firebase-adminsdk.json"
```

En producción (Render u otro), usa variables de entorno con doble guion bajo:
`OpenAI__ApiKey`, `ElevenLabs__ApiKey`, `Deepgram__ApiKey`, `Firebase__ApiKey`
y `GOOGLE_APPLICATION_CREDENTIALS_JSON` con el contenido del JSON de la cuenta de servicio.
