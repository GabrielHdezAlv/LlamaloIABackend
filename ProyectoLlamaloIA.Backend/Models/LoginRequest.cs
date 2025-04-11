using System.ComponentModel.DataAnnotations;
using Google.Cloud.Firestore;

namespace ProyectoLlamaloIA.Backend.Models;

public class LoginRequest
{
    [Required(ErrorMessage = "Ingrese un Nombre de Usuario")]
    [EmailAddress(ErrorMessage = "Ingrese un Email")]
    [FirestoreProperty("email")]
    public string email { get; set; }
    
    [Required(ErrorMessage = "La contraseña es obligatoria")]
    [FirestoreProperty("password")]
    public string password { get; set; }
}