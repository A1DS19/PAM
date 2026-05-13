using Microsoft.EntityFrameworkCore;
using Pam.Operators.Brands.Exceptions;
using Pam.Operators.Brands.Models;
using Pam.Operators.Data;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Operators.Brands.Features.CreateBrand;

public sealed class CreateBrandHandler(OperatorsDbContext db)
    : ICommandHandler<CreateBrandCommand, Guid>
{
    public async Task<Guid> Handle(CreateBrandCommand command, CancellationToken cancellationToken)
    {
        var slugTaken = await db
            .Brands.AsNoTracking()
            .AnyAsync(b => b.Slug == command.Slug, cancellationToken);

        if (slugTaken)
        {
            throw new AlreadyExistsException(
                BrandErrors.SlugTaken,
                $"A brand with slug '{command.Slug}' already exists."
            );
        }

        var brand = Brand.Create(command.Name, command.Slug, command.Jurisdiction);
        db.Brands.Add(brand);
        await db.SaveChangesAsync(cancellationToken);
        return brand.Id;
    }
}
