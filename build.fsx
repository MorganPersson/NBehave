// include Fake libs
#I "buildframework/FAKE/tools"
#r "FakeLib.dll"

#I "src/packages/DotNetZip/lib/net20"
#r "Ionic.Zip.dll"

#load "buildProperties.fsx"

open Properties
open Fake
open Fake.RestorePackageHelper
open System
open System.Diagnostics
open System.IO
open System.Net
open System.Text.RegularExpressions
open System.Xml

// Targets
Target "Clean" (fun _ ->
  killMSBuild()
  CleanDir buildDir
  CleanDirs [testReportsDir; artifactsDir]
)

Target "Restore nuget packages" (fun _ ->
  !! (sourceDir + "/**/packages.config")
  |> Seq.iter (RestorePackage (fun p ->
                { p with
                    ToolPath = nugetExe
                    OutputPath = nugetPackageDir
                }))
)

Target "InstallNUnitRunners" (fun _ ->
  let settings = { RestoreSinglePackageDefaults with
                    ToolPath = nugetExe
                    OutputPath = nugetPackageDir
                    ExcludeVersion = true
                  }
  RestorePackageId (fun p -> settings) "nunit.runners"
)

Target "Set teamcity buildnumber" (fun _ ->
  SetBuildNumber nugetVersionNumber
)


let ReSharperSdkInstall version (urlToSdk:string) =
  let sdkPath = rootDir + @"lib\ReSharper\" + version
  if (Directory.Exists sdkPath) then
    trace (sprintf "R# SDK %s already installed." version)
  else
    // download SDK
    trace (sprintf "Downloading R# SDK %s ..." version)
    let wc = new WebClient()
    let downloadedFile = rootDir + "RSharperSDK-" + version + ".zip"
    wc.DownloadFile(urlToSdk, downloadedFile)
    (
      use zip = new Ionic.Zip.ZipFile(downloadedFile)
      trace "Extracting files..."
      zip.ExtractAll(rootDir + @"lib\ReSharper\" + version)
    )
    File.Delete(downloadedFile)

let ReSharperSdkPath version =
  // Search rootDir + "\lib" efter Plugin.Common.Targets och fixa alla
  let sdkPath = rootDir + @"lib\ReSharper\" + version

  let fileName = sdkPath + @"\Targets\Plugin.Common.Targets"
  let xml = XmlDocument()
  xml.Load(fileName)
  let nsmgr = XmlNamespaceManager(xml.NameTable)
  nsmgr.AddNamespace("x", "http://schemas.microsoft.com/developer/msbuild/2003")
  //multiple nodes?
  let node = xml.SelectSingleNode("//x:ReSharperSdk", nsmgr)
  node.InnerText <- sdkPath
  xml.Save(fileName)

Target "Install R# 7 SDK" (fun _ ->
  let version = "7.1.96"
  ReSharperSdkInstall version ("http://download.jetbrains.com/resharper/ReSharperSDK-" + version + ".zip")
  ReSharperSdkPath version
)

Target "Install R# 8 SDK" (fun _ ->
  let version = "8.0.1086"
  ReSharperSdkInstall version ("http://download.jetbrains.com/resharper/ReSharperSDK-" + version + ".zip")
  ReSharperSdkPath version
)

Target "AssemblyInfo" (fun _ ->
  let fileName = (sourceDir + "/CommonAssemblyInfo.cs") |> FullName
  ReplaceAssemblyInfoVersions (fun p ->
    { p with
        AssemblyVersion = assemblyVersion
        AssemblyFileVersion = assemblyVersion
        AssemblyInformationalVersion = assemblyInfoVersion
        OutputFileName = fileName
    })

  //Version for source.extension.vsixmanifest
  let vsixFile = (sourceDir + "/NBehave.VS2010.Plugin/source.extension.vsixmanifest") |> FullName
  let xml = new XmlDocument()
  xml.Load(vsixFile)
  let nsmgr = new XmlNamespaceManager(xml.NameTable)
  nsmgr.AddNamespace("x", @"http://schemas.microsoft.com/developer/vsx-schema/2010")
  let node = xml.SelectSingleNode(@"x:Vsix/x:Identifier/x:Version", nsmgr)
  node.InnerText <- assemblyVersion
  xml.Save(vsixFile)

  //Version for VS plugin package
  let package =  sourceDir + @"\NBehave.VS2010.Plugin\NBehaveRunnerPackage.cs"
  let content = File.ReadAllLines(package)
  let changeVersion (str:string) =
    match str.Contains("[InstalledProductRegistration(") with
      | true  -> Regex.Replace(str, @"\d+\.\d+\.\d+", version)
      | false -> str
  let rows = content |> Seq.map (fun l -> changeVersion l)
  File.WriteAllLines(package, rows)
)

let outputPath frameworkVer =
  (buildDir + "/Debug-" + frameworkVer + "/NBehave") |> FullName

let compileAnyCpu frameworkVer =
  let sln = sourceDir + "/NBehave.sln"
  build (fun f ->
          { f with
              MaxCpuCount = Some (Some Environment.ProcessorCount)
              ToolsVersion = Some "4.0"
              Verbosity = Some MSBuildVerbosity.Minimal
              Properties =  [ ("Configuration", "AutomatedDebug-" + frameworkVer);
                              ("TargetFrameworkVersion", "v" + frameworkVer)
                            ]
              Targets = ["Rebuild"]
          }) sln

let compileConsolex86 frameworkVer =
  let sln = sourceDir + @"\NBehave.Console\NBehave.Console.csproj"
  let folder = outputPath frameworkVer
  build (fun f ->
          { f with
              MaxCpuCount = Some (Some Environment.ProcessorCount)
              ToolsVersion = Some "4.0"
              Verbosity = Some MSBuildVerbosity.Minimal
              Properties =  [ ("Configuration", "AutomatedDebug-" + frameworkVer + "-x86")
                              ("TargetFrameworkVersion", "v" + frameworkVer)
                              ("OutputPath", folder)
                            ]
              Targets = ["Rebuild"]
          }) sln
  Rename (folder + "/NBehave-Console-x86.exe") (folder + "/NBehave-Console.exe")

Target "Compile" (fun _ ->
  frameworkVersions |> Seq.iter (fun v ->
                                  compileConsolex86 v
                                  compileAnyCpu v)
)


Target "Test" (fun _ ->
  frameworkVersions
  |> Seq.iter(fun frameworkVer ->
                let testDir = (buildDir + "/Debug-" + frameworkVer + "/UnitTests") |> FullName
                let testDlls = !! (testDir + "/*.Specifications.dll")
                let xmlFile = (testReportsDir + @"\UnitTests-" + frameworkVer + ".xml") |> FullName
                NUnit (fun p ->
                        {p with
                          ToolPath = (nugetPackageDir + "/NUnit.Runners/tools/") |> FullName
                          OutputFile = xmlFile
                          Framework = frameworkVer
                          ShowLabels = false
                        }) testDlls
                sendTeamCityNUnitImport xmlFile
              )
)

Target "VSPlugin artifact" (fun _ ->
  !! (buildDir + "/**/*.vsix")
  |> CopyFiles artifactsDir
)

let resharper_install_scripts (version:string) =
  let content = File.ReadAllText (packageTemplateDir + "/Resharper.scripts/Install.ps1")
  let out = Regex.Replace(content, @"\%version\%", version)
  File.WriteAllText (buildDir + "/Install.ps1", out)
  let content = File.ReadAllText (packageTemplateDir + "/Resharper.scripts/UnInstall.ps1")
  let out = Regex.Replace(content, @"\%version\%", version)
  File.WriteAllText ( buildDir + "/UnInstall.ps1", out)

let nugetParams p =
  { p with
      ToolPath = nugetExe
      Version = nugetVersionNumber
      OutputPath = artifactsDir
      WorkingDir = artifactsDir
      AccessKey = nugetAccessKey
  }

Target "Create NuGet packages" (fun _ ->
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.runners.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.samples.nuspec")
)

Target "Create NuGet packages for R#" (fun _ ->
  resharper_install_scripts "7.1"
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.Resharper71.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.Resharper711.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.Resharper712.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.Resharper80.nuspec")
)

Target "Create NuGet packages Fluent" (fun _ ->
  NuGetPack (fun p -> { (nugetParams p) with Properties = ["nunitVersion", nunitVersion()] } )
            (packageTemplateDir + "/nbehave.Fluent.NUnit.nuspec")
  NuGetPack (fun p -> { (nugetParams p) with Properties = ["xunitVersion", xunitVersion()] } )
            (packageTemplateDir + "/nbehave.Fluent.XUnit.nuspec")
  NuGetPack (fun p -> { (nugetParams p) with Properties = ["mbunitVersion", mbUnitVersion()] } )
            (packageTemplateDir + "/nbehave.Fluent.MbUnit.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.Fluent.MsTest.nuspec")
)

Target "Create NuGet packages Spec" (fun _ ->
  NuGetPack (fun p -> { (nugetParams p) with Properties = ["nunitVersion", nunitVersion()] } )
            (packageTemplateDir + "/nbehave.Spec.NUnit.nuspec")
  NuGetPack (fun p -> { (nugetParams p) with Properties = ["xunitVersion", xunitVersion()] } )
            (packageTemplateDir + "/nbehave.Spec.XUnit.nuspec")
  NuGetPack (fun p -> { (nugetParams p) with Properties = ["mbunitVersion", mbUnitVersion()] } )
            (packageTemplateDir + "/nbehave.Spec.MbUnit.nuspec")
  NuGetPack nugetParams (packageTemplateDir + "/nbehave.Spec.MsTest.nuspec")
  ()
)

Target "Publish to NuGet" (fun _ ->
  //Get-ChildItem -Path $artifactsDir -Filter *.nupkg | Select FullName | foreach-object {
  //  $package = $_.FullName
  //  Write-Host "Publishing package: $package"
  //  Exec { src\.nuget\NuGet.exe "push" $package $apiKey }
  ()
)

Target "Default" (fun _ -> () )

// Dependencies
"Clean"
  ==> "Set teamcity buildnumber"
  ==> "Install R# 7 SDK"
  ==> "Install R# 8 SDK"
  ==> "Restore nuget packages"
  ==> "InstallNUnitRunners"
  ==> "AssemblyInfo"
  ==> "Compile"
  ==> "Test"
  ==> "VSPlugin artifact"
  ==> "Create NuGet packages"
  ==> "Create NuGet packages for R#"
  ==> "Create NuGet packages Fluent"
  ==> "Create NuGet packages Spec"
  ==> "Default"

// Start build
Run <| getBuildParamOrDefault "target" "Default"
