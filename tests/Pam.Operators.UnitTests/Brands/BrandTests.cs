using FluentAssertions;
using Pam.Operators.Brands.Events;
using Pam.Operators.Brands.Models;
using Pam.Operators.Contracts.Brands.Models;
using Xunit;

namespace Pam.Operators.UnitTests.Brands;

public class BrandTests
{
    [Fact]
    public void Create_returns_active_brand_with_id()
    {
        var brand = Brand.Create("BetAnything EU", "betanything-eu", "EU");

        brand.Id.Should().NotBe(Guid.Empty);
        brand.Name.Should().Be("BetAnything EU");
        brand.Slug.Should().Be("betanything-eu");
        brand.Jurisdiction.Should().Be("EU");
        brand.Status.Should().Be(BrandStatus.Active);
    }

    [Fact]
    public void Create_raises_brand_created_domain_event()
    {
        var brand = Brand.Create("BetAnything EU", "betanything-eu", "EU");

        brand.DomainEvents.Should().HaveCount(1);
        var evt = brand.DomainEvents[0].Should().BeOfType<BrandCreatedDomainEvent>().Subject;
        evt.BrandId.Should().Be(brand.Id);
        evt.Name.Should().Be("BetAnything EU");
        evt.Slug.Should().Be("betanything-eu");
        evt.Jurisdiction.Should().Be("EU");
    }

    [Fact]
    public void ClearDomainEvents_returns_and_empties_list()
    {
        var brand = Brand.Create("BetAnything EU", "betanything-eu", "EU");

        var cleared = brand.ClearDomainEvents();

        cleared.Should().HaveCount(1);
        brand.DomainEvents.Should().BeEmpty();
    }
}
