using Google.Cloud.Firestore;

namespace ProyectoLlamaloIA.Backend.Models;

[FirestoreData]
public class Language
{
    [FirestoreProperty("flag")]
    public string Flag { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("code")]
    public string Code { get; set; }

    // Constructor sin parámetros para permitir que Firestore cree la instancia
    public Language() { }

    // Constructor con parámetros (opcional, pero útil para instanciar manualmente)
    public Language(string flag, string name, string code)
    {
        Flag = flag;
        Name = name;
        Code = code;
    }
}