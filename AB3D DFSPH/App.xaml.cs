using System.Windows;


namespace AB3D_DFSPH
{

    public partial class App : Application
    {

        public App()
        {
            System.IO.Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // Activate license for Ab3d.PowerToys:
            // The license is valid for all versions that are published before Oct 16, 2026.
            // To use versions after that date, purchase a license renewal and regenerate this activation code.
            Ab3d.Licensing.PowerToys.LicenseHelper.SetLicense(licenseOwner: "Bio Molecular Systems Pty Ltd",
                                                              licenseType: "SiteDeveloperLicense",
                                                              license: "FD80-2D54-0DCF-E37D-517D-DA9E-DFDF-CE4D-248F-0F7B-2F1E-9B6F-D060-2EE4-4462-E3FB");
            // Activate license for Ab3d.DXEngine:
            // The license is valid for all versions that are published before Aug 29, 2026.
            // To use versions after that date, purchase a license renewal and regenerate this activation code.
            Ab3d.Licensing.DXEngine.LicenseHelper.SetLicense(licenseOwner: "Bio Molecular Systems Pty Ltd",
                                                             licenseType: "SiteDeveloperLicense",
                                                             license: "9E5D-18B5-D24D-DCAA-E8FB-9F5F-FD40-C7F2-5269-8601-E3E7-726D-0604-CD94-271C-A8DB");
        }

    }

}
