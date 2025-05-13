using Google.Cloud.Firestore;

namespace ProyectoLlamaloIA.Backend.Models;

[FirestoreData]
public class Agent
{
    [FirestoreProperty("labsId")]
    public string LasbId { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("photoUrl")]
    public string PhotoUrl { get; set; }

    // Constructor sin parámetros para permitir que Firestore cree la instancia
    public Agent() { }

    // Constructor con parámetros (opcional, pero útil para instanciar manualmente)
    public Agent(string labsId, string name, string photoUrl)
    {
        LasbId = labsId;
        Name = name;
        PhotoUrl = photoUrl;
    }
}