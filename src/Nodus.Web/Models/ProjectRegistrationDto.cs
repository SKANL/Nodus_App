using System.ComponentModel.DataAnnotations;

namespace Nodus.Web.Models;

public class ProjectRegistrationDto
{
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre del proyecto es obligatorio")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La categoría es obligatoria")]
    public string Category { get; set; } = string.Empty;

    [Required(ErrorMessage = "La descripción es obligatoria")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Los integrantes son obligatorios")]
    public string Members { get; set; } = string.Empty;

    public string? GithubUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
