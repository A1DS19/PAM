namespace Pam.Players.Players.Brands;

public interface IBrandRegistry
{
    bool IsRegistered(string brandId);

    string GetOrgId(string brandId);
}
