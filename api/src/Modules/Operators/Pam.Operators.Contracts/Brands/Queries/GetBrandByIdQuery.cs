using Pam.Operators.Contracts.Brands.Dtos;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Operators.Contracts.Brands.Queries;

public sealed record GetBrandByIdQuery(Guid BrandId) : IQuery<BrandDto>;
