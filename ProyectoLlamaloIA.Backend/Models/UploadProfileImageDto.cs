using System.ComponentModel.DataAnnotations;

namespace ProyectoLlamaloIA.Backend.Models;

public class UploadProfileImageDto
{
    [Required]
    public IFormFile Image { get; set; }

    // Puedes agregar más campos opcionales si quieres
    public string? Description { get; set; }
}