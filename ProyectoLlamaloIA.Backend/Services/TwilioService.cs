using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace ProyectoLlamaloIA.Backend.Services;

public class TwilioService
{
    private readonly ILogger<TwilioService> _logger;
    private readonly string _voiceWebhookUrl;
    
    public TwilioService(IConfiguration config, ILogger<TwilioService> logger)
    {
        _logger = logger;
        _voiceWebhookUrl = config["Twilio:WebhookUrl"] ?? throw new Exception("Falta Twilio:WebhookUrl en configuración");
    }
    
    public async Task<bool> ConfigureNumberWebhookAsync(string accountSid, string authToken, string phoneNumber)
    {
        try
        {
            TwilioClient.Init(accountSid, authToken);

            var phoneNumbers = await IncomingPhoneNumberResource.ReadAsync(limit: 50);

            var match = phoneNumbers.FirstOrDefault(p => p.PhoneNumber.ToString() == phoneNumber);

            if (match == null)
            {
                _logger.LogError("No se encontró el número {PhoneNumber} en la cuenta Twilio", phoneNumber);
                return false;
            }

            await IncomingPhoneNumberResource.UpdateAsync(
                pathSid: match.Sid,
                voiceUrl: new Uri(_voiceWebhookUrl),
                voiceMethod: Twilio.Http.HttpMethod.Post
            );

            _logger.LogInformation("Webhook configurado en Twilio para {PhoneNumber}", phoneNumber);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al configurar webhook en Twilio");
            return false;
        }
    }
}