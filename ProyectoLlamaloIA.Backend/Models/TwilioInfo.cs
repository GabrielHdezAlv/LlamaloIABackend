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

    // Constructor para inicializar las propiedades de TwilioInfo
    public TwilioInfo(string countryCode, long phoneNumber, string twilioSid, string twilioToken)
    {
        CountryCode = countryCode;
        PhoneNumber = phoneNumber;
        TwilioSid = twilioSid;
        TwilioToken = twilioToken;
    }
}