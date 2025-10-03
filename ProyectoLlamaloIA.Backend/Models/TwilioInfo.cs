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
    
    [FirestoreProperty("agentInstruction")]
    public string? AgentInstruction { get; set; }
    
    // ✅ Constructor vacío necesario para Firestore
    public TwilioInfo() {}

    // Constructor para inicializar las propiedades de TwilioInfo
    public TwilioInfo(string countryCode, long phoneNumber, string twilioSid, string twilioToken, string firstInteraction, string firstInteractionUrl, string agentInstruction)
    {
        CountryCode = countryCode;
        PhoneNumber = phoneNumber;
        TwilioSid = twilioSid;
        TwilioToken = twilioToken;
        FirstInteraction = firstInteraction;
        FirstInteractionUrl = firstInteractionUrl;
        AgentInstruction = agentInstruction;
    }
}