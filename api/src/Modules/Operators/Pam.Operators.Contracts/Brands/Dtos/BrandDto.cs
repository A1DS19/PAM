using Pam.Operators.Contracts.Brands.Models;

namespace Pam.Operators.Contracts.Brands.Dtos;

public sealed record BrandDto(
    Guid Id,
    string Name,
    string Slug,
    string Jurisdiction,
    BrandStatus Status,
    DateTimeOffset CreatedAt
);
