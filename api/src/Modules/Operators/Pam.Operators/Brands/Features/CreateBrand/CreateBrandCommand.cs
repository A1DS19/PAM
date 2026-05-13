using Pam.Shared.Contracts.CQRS;

namespace Pam.Operators.Brands.Features.CreateBrand;

public sealed record CreateBrandCommand(string Name, string Slug, string Jurisdiction)
    : ICommand<Guid>;
