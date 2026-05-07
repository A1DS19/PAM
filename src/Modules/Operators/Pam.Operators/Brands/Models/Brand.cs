using Pam.Operators.Brands.Events;
using Pam.Operators.Contracts.Brands.Models;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.DDD;

namespace Pam.Operators.Brands.Models;

public sealed class Brand : Aggregate<Guid>
{
    public string Name { get; private set; } = default!;

    public string Slug { get; private set; } = default!;

    public string Jurisdiction { get; private set; } = default!;

    public BrandStatus Status { get; private set; }

    private Brand() { }

    public static Brand Create(string name, string slug, string jurisdiction)
    {
        var brand = new Brand
        {
            Id = PamIds.New(),
            Name = name,
            Slug = slug,
            Jurisdiction = jurisdiction,
            Status = BrandStatus.Active,
        };
        brand.RaiseDomainEvent(
            new BrandCreatedDomainEvent(brand.Id, brand.Name, brand.Slug, brand.Jurisdiction)
        );
        return brand;
    }
}
