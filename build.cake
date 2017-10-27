//////////////////////////////////////////////////////////////////////
// ADDINS
//////////////////////////////////////////////////////////////////////

#addin "Cake.FileHelpers"

//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

#tool GitVersion.CommandLine
#tool GitLink
#tool xunit.runner.console

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Publish");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// should MSBuild & GitLink treat any errors as warnings.
var treatWarningsAsErrors = false;

// Get whether or not this is a local build.
var local = BuildSystem.IsLocalBuild;
var isRunningOnUnix = IsRunningOnUnix();
var isRunningOnWindows = IsRunningOnWindows();

//var isRunningOnBitrise = Bitrise.IsRunningOnBitrise;
var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;

var isRepository = StringComparer.OrdinalIgnoreCase.Equals("ghuntley/Cake.AndroidAppManifest", AppVeyor.Environment.Repository.Name);

// Parse release notes.
var releaseNotes = ParseReleaseNotes("RELEASENOTES.md");

// Get version.
var version = releaseNotes.Version.ToString();
var epoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
var gitSha = GitVersion().Sha;

var semVersion = local ? string.Format("{0}.{1}", version, epoch) : string.Format("{0}.{1}", version, epoch);

// Define directories.
var artifactDirectory = "./artifacts/";

// Define global marcos.
Action Abort = () => { throw new Exception("a non-recoverable fatal error occurred."); };

Action<string> RestorePackages = (solution) =>
{
    NuGetRestore(solution, new NuGetRestoreSettings() { ConfigFile = "./src/.nuget/NuGet.config" });
};

Action<string> SourceLink = (solutionFileName) =>
{
    GitLink("./", new GitLinkSettings() {
        RepositoryUrl = "https://github.com/ghuntley/Cake.AndroidAppManifest",
        SolutionFileName = solutionFileName,
        ErrorsAsWarnings = treatWarningsAsErrors,
    });
};


///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup((ctx) =>
{
    Information("Building version {0} of Cake.AndroidAppManifest.", semVersion);
});

Teardown((ctx) =>
{
    // Executed AFTER the last task.
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Build")
    .IsDependentOn("RestorePackages")
    .IsDependentOn("UpdateAssemblyInfo")
    .Does (() =>
{
    Action<string> build = (filename) =>
    {
        var solution = System.IO.Path.Combine("./src/", filename);

        // UWP (project.json) needs to be restored before it will build.
        RestorePackages(solution);

        Information("Building {0}", solution);

        MSBuild(solution, new MSBuildSettings()
            .SetConfiguration(configuration)
            .WithProperty("NoWarn", "1591") // ignore missing XML doc warnings
            .WithProperty("TreatWarningsAsErrors", treatWarningsAsErrors.ToString())
            .SetVerbosity(Verbosity.Minimal)
            .SetNodeReuse(false));

        //Information("Running GitLink for {0}", solution);
        //SourceLink(solution);
    };

    build("Cake.AndroidAppManifest.sln");
});

Task("UpdateAppVeyorBuildNumber")
    .WithCriteria(() => isRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UpdateBuildVersion(semVersion);
});

Task("UpdateAssemblyInfo")
    .IsDependentOn("UpdateAppVeyorBuildNumber")
    .Does (() =>
{
    var file = "./src/CommonAssemblyInfo.cs";

    CreateAssemblyInfo(file, new AssemblyInfoSettings {
        Product = "Cake.AndroidAppManifest",
        Version = version,
        FileVersion = version,
        InformationalVersion = semVersion,
        Copyright = "Copyright (c) Geoffrey Huntley"
    });
});

Task("RestorePackages").Does (() =>
{
    RestorePackages("./src/Cake.AndroidAppManifest.sln");
});

Task("RunUnitTests")
    .IsDependentOn("Build")
    .Does(() =>
{
    XUnit2("./src/Cake.AndroidAppManifest.Tests/bin/Release/Cake.AndroidAppManifest.Tests.dll", new XUnit2Settings {
        OutputDirectory = artifactDirectory,
        XmlReportV1 = false,
        NoAppDomain = true
    });
});

Task("Package")
    .IsDependentOn("Build")
    .IsDependentOn("RunUnitTests")
    .Does (() =>
{
    // switched to msbuild-based nuget creation
    // see here for parameters: https://docs.microsoft.com/en-us/nuget/schema/msbuild-targets
    MSBuild ("./src/Cake.AndroidAppManifest/Cake.AndroidAppManifest.csproj", c => {
		c.Configuration = configuration;
		c.Targets.Add ("pack");
		c.Properties.Add ("IncludeSymbols", new List<string> { "true" });
		c.Properties.Add ("PackageReleaseNotes", new List<string>(releaseNotes.Notes));
	});
});

Task("Publish")
    .IsDependentOn("Package")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isRepository)
    .Does (() =>
{
    // Resolve the API key.
    var apiKey = EnvironmentVariable("MYGET_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new InvalidOperationException("Could not resolve MyGet API key.");
    }

    // only push whitelisted packages.
    foreach(var package in new[] { "Cake.AndroidAppManifest" })
    {
        // only push the package which was created during this build run.
        var packagePath = artifactDirectory + File(string.Concat(package, ".", semVersion, ".nupkg"));
        //var symbolsPath = artifactDirectory + File(string.Concat(package, ".", semVersion, ".symbols.nupkg"));

        // Push the package.
        NuGetPush(packagePath, new NuGetPushSettings {
            Source = "https://www.myget.org/F/ghuntley/api/v2/package",
            ApiKey = apiKey
        });

        // Push the symbols
        //NuGetPush(symbolsPath, new NuGetPushSettings {
        //    Source = "https://www.myget.org/F/ghuntley/api/v2/package",
        //    ApiKey = apiKey
        //});

    }
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////


//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
