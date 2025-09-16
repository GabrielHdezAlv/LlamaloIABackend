using Google.Cloud.Firestore;

namespace ProyectoLlamaloIA.Backend.Models;

[FirestoreData]
public class User : FirebaseDocument
{
    [FirestoreProperty("userId")]
    public string userId { get; set; }
    
    [FirestoreProperty("name")]
    public string name { get; set; }

    [FirestoreProperty("lastname")]
    public string lastname { get; set; }
    
    [FirestoreProperty("profileImage")]
    public string profileImage { get; set; }
    
    [FirestoreProperty("enterprise")]
    public string enterprise { get; set; }
    
    [FirestoreProperty("enterpriseWeb")]
    public string enterpriseWeb { get; set; }

    [FirestoreProperty("email")]
    public string email { get; set; }

    [FirestoreProperty("password")]
    public string password { get; set; }

    [FirestoreProperty("createdAt")]
    public Timestamp createdAt { get; set; }

    [FirestoreProperty("twilioInfo")]
    public TwilioInfo twilio { get; set; }
    
    [FirestoreProperty("agent")]
    public Agent agent { get; set; }
    
}