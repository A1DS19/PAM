using Microsoft.EntityFrameworkCore;
using Pam.Operators.Brands.Exceptions;
using Pam.Operators.Contracts.Brands.Dtos;
using Pam.Operators.Contracts.Brands.Queries;
using Pam.Operators.Data;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Operators.Brands.Features.GetBrand;

public sealed class GetBrandQueryHandler(OperatorsDbContext db)
    : IQueryHandler<GetBrandByIdQuery, BrandDto>
{
    public async Task<BrandDto> Handle(GetBrandByIdQuery query, CancellationToken cancellationToken)
    {
        var brand = await db
            .Brands.AsNoTracking()
            .Where(b => b.Id == query.BrandId)
            .Select(b => new BrandDto(
                b.Id,
                b.Name,
                b.Slug,
                b.Jurisdiction,
                b.Status,
                b.CreatedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (brand is null)
        {
            throw new NotFoundException(
                BrandErrors.NotFound,
                $"Brand {query.BrandId} not found."
            );
        }

        return brand;
    }
}
