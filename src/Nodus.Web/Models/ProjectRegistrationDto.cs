using System.ComponentModel.DataAnnotations;

namespace Nodus.Web.Models;

public class ProjectRegistrationDto
{
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name is too long")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category is required")]
    public string Category { get; set; } = "Software";

    [Required(ErrorMessage = "Description is required")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Authors are required")]
    public string Authors { get; set; } = string.Empty;

    public string? GithubUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsSynced { get; set; } = false;
}
