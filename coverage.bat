@echo off
setlocal
rem Run unit tests with Microsoft.CodeCoverage, merge to Cobertura, optional ReportGenerator HTML.
pushd "%~dp0"

echo Running tests (net10.0) with Microsoft Code Coverage...
dotnet test AmbientServices.Async.Test -c Release -f net10.0 --collect:"Code Coverage" --settings codecoverage.runsettings
if errorlevel 1 goto :fail

echo Merging .coverage to coverage.cobertura.xml...
dotnet-coverage merge "**/TestResults/**/*.coverage" -f cobertura -o coverage.cobertura.xml
if errorlevel 1 goto :fail

where reportgenerator >nul 2>&1
if errorlevel 1 (
  echo reportgenerator not on PATH; skipped HTML. Install: dotnet tool install -g dotnet-reportgenerator-globaltool
) else (
  echo Generating coveragereport (HTML + badges^)...
  reportgenerator -reports:coverage.cobertura.xml -targetdir:coveragereport -reporttypes:HtmlInline;Badges
  if errorlevel 1 goto :fail
)

if exist coveragereport\badge_linecoverage.svg (
  echo Updating assets\badge_linecoverage.svg for NuGet package readme...
  if not exist assets mkdir assets
  copy /y coveragereport\badge_linecoverage.svg assets\badge_linecoverage.svg >nul
)

echo.
echo Output: coverage.cobertura.xml
if exist coveragereport\index.html echo HTML:  coveragereport\index.html
if exist assets\badge_linecoverage.svg echo Badge: assets\badge_linecoverage.svg
popd
exit /b 0

:fail
echo coverage.bat: failed.
popd
exit /b 1
