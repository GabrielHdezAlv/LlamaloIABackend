using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace ProyectoLlamaloIA.Backend.Models;

[FirestoreData]
public class AgentConfig
{
    [FirestoreDocumentId]
    public string UserId { get; set; }

    [FirestoreProperty("twilio")]
    public Dictionary<string, object> Twilio { get; set; } = new Dictionary<string, object>();

    [FirestoreProperty("agent")]
    public Dictionary<string, object> Agent { get; set; } = new Dictionary<string, object>();
}