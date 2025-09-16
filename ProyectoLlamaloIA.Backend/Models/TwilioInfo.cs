using Google.Cloud.Firestore;

namespace ProyectoLlamaloIA.Backend.Models;

[FirestoreData]
public class TwilioInfo
{
    [FirestoreProperty("countryCode")]
    public string CountryCode { get; set; }

    [FirestoreProperty("phoneNumber")]
    public long PhoneNumber { get; set; }

    [FirestoreProperty("twilioSid")]
    public string TwilioSid { get; set; }

    [FirestoreProperty("twilioToken")]
    public string TwilioToken { get; set; }
    
    [FirestoreProperty("firstInteraction")]
    public string? FirstInteraction { get; set; }
    
    [FirestoreProperty("firstInteractionUrl")]
    public string? FirstInteractionUrl { get; set; }

    // Constructor para inicializar las propiedades de TwilioInfo
    public TwilioInfo(string countryCode, long phoneNumber, string twilioSid, string twilioToken, string firstInteraction, string firstInteractionUrl)
    {
        CountryCode = countryCode;
        PhoneNumber = phoneNumber;
        TwilioSid = twilioSid;
        TwilioToken = twilioToken;
        FirstInteraction = firstInteraction;
        FirstInteractionUrl = firstInteractionUrl;
    }
}