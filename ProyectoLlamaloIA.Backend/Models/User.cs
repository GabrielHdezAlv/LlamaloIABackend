using Google.Cloud.Firestore;

namespace ProyectoLlamaloIA.Backend.Models;

[FirestoreData]
public class User : FirebaseDocument
{
    [FirestoreProperty("name")]
    public string name { get; set; }

    [FirestoreProperty("lastname")]
    public string lastname { get; set; }

    [FirestoreProperty("email")]
    public string email { get; set; }

    [FirestoreProperty("password")]
    public string password { get; set; }

    [FirestoreProperty("created_at")]
    public DateTime created_at { get; set; }

    [FirestoreProperty("twilioInfo")]
    public TwilioInfo twilio { get; set; }
}