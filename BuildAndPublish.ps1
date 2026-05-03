# Required environment variables $env:version, $env:nugetapikey

"`nCleaning the workspace`n---------------------------" 
Remove-Item -Recurse -Force AmbientServices.Async.Test\TestResults -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force AmbientServices.Async.Test.DelayedLoad\TestResults -ErrorAction SilentlyContinue

"`nSetting Version: $version`n---------------------------" 
"`ndotnet .\SetVersion.dll -- $version"
dotnet .\SetVersion.dll -- $version

"`nBuilding Binaries`n---------------------------" 
dotnet build -c Release
if (!$?) { exit 1 }

"`nRunning Tests with Coverage`n---------------------------" 
# Microsoft.CodeCoverage (Visual Studio 2026 / dotnet test). Optional: dotnet tool install --global dotnet-coverage
dotnet test AmbientServices.Async.Test -f net10.0 --collect:"Code Coverage" --settings codecoverage.runsettings --logger:"trx;LogFileName=unit.testresults.trx"
$testResult = $?
if (!$testResult) {exit 1}

"`n`nCreating Nuget Package`n---------------------------" 
dotnet pack -c Release
if (!$?) { exit 1 }

"`n`nPublishing to NuGet`n---------------------------" 
dotnet nuget push AmbientServices.Async\bin\Release\AmbientServices.Async.$version.nupkg -k $env:nugetapikey -s https://api.nuget.org/v3/index.json
if (!$?) { exit 1 }
