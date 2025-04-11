using Google.Cloud.Firestore;
using ProyectoLlamaloIA.Backend.Models;

namespace ProyectoLlamaloIA.Backend.Repository;

public class UserRepository
{
    private readonly string CollectionName = "users";
    private FirestoreDb firestoreDb;

    public User Add(User model)
    {
        try
        {
            CollectionReference collectionReference = firestoreDb.Collection(CollectionName);
            DocumentReference newDocument = collectionReference.AddAsync(model).GetAwaiter().GetResult();
            model.Id = newDocument.Id;
            return model;
        }
        catch (Exception e)
        {
            return null;
        }
    }

    public User Get(User record)
    {
        DocumentReference docRef = firestoreDb.Collection(CollectionName).Document(record.Id);
        DocumentSnapshot snapshot = docRef.GetSnapshotAsync().GetAwaiter().GetResult();
        if (snapshot.Exists)
        {
            User usr = snapshot.ConvertTo<User>();
            usr.Id = snapshot.Id;
            return usr;
        }
        else
        {
            return null;
        }
    }
}