namespace Wintime.Control.SDK.Licensing;

public interface ILicenseValidator
{
    IModuleLicense ValidateFromFile(string licenseFilePath, string installationId);
    IModuleLicense ValidateFromString(string licenseData, string installationId);
}
