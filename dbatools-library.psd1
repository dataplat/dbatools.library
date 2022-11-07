#
# Module manifest for module 'dbatools-library'
#
# Generated by: Chrissy LeMaire
#
# Generated on: 10/12/2022
#
@{
    # Version number of this module.
    ModuleVersion          = '2022.10.28'

    # ID used to uniquely identify this module
    GUID                   = '00b61a37-6c36-40d8-8865-ac0180288c84'

    # Author of this module
    Author                 = 'the dbatools team'

    # Company or vendor of this module
    CompanyName            = 'dbatools.io'

    # Copyright statement for this module
    Copyright              = 'Copyright (c) 2022 by dbatools, licensed under MIT'

    # Description of the functionality provided by this module
    Description            = "The library that powers dbatools, the community module for SQL Server Pros"

    # Minimum version of the Windows PowerShell engine required by this module
    PowerShellVersion      = '5.1'

    # Minimum version of the .NET Framework required by this module
    DotNetFrameworkVersion = '4.6.2'

    # Supported PSEditions
    CompatiblePSEditions   = @('Desktop')

    # Modules that must be imported into the global environment prior to importing this module
    RequiredModules        = @()

    # Assemblies that must be loaded prior to importing this module
    # DO NOT BE TEMPTED to load SQL Server assemblies here
    # because SQL Server has so many diff componenets
    # (SMO/DacFX/SqlClient), we need to first load bindingRedirects
    # but only in Full .NET because Core does something different
    RequiredAssemblies     = @()

    # Script module or binary module file associated with this manifest.
    RootModule             = 'dbatools-library.psm1'

    FunctionsToExport      = @('Get-DbatoolsLibraryPath')

    PrivateData            = @{
        # PSData is module packaging and gallery metadata embedded in PrivateData
        # It's for rebuilding PowerShellGet (and PoshCode) NuGet-style packages
        # We had to do this because it's the only place we're allowed to extend the manifest
        # https://connect.microsoft.com/PowerShell/feedback/details/421837
        PSData = @{
            # The primary categorization of this module (from the TechNet Gallery tech tree).
            Category     = "Databases"

            # Keyword tags to help users find this module via navigations and search.
            Tags         = @('sqlserver', 'migrations', 'sql', 'dba', 'databases', 'mac', 'linux', 'core', 'smo')

            # The web address of an icon which can be used in galleries to represent this module
            IconUri      = "https://dbatools.io/logo.png"

            # The web address of this module's project or support homepage.
            ProjectUri   = "https://dbatools.io"

            # The web address of this module's license. Points to a page that's embeddable and linkable.
            LicenseUri   = "https://opensource.org/licenses/MIT"

            # Release notes for this particular version of the module
            ReleaseNotes = ""

            # If true, the LicenseUrl points to an end-user license (not just a source license) which requires the user agreement before use.
            # RequireLicenseAcceptance = ""

            # Indicates this is a pre-release/testing version of the module.
            IsPrerelease = 'true'
            Prerelease   = 'preview'
        }
    }
}