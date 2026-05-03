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

if exist coveragereport\badge_shieldsio_linecoverage_green.svg (
  echo Updating docs\line-coverage.svg from docs\line-coverage.template.svg...
  if not exist docs mkdir docs
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$raw = Get-Content 'coveragereport\badge_shieldsio_linecoverage_green.svg' -Raw; ^
     $m = [regex]::Match($raw, '[0-9]+(\.[0-9]+)?%%'); ^
     if (-not $m.Success) { exit 1 }; ^
     $p = $m.Value; ^
     $t = Get-Content 'docs\line-coverage.template.svg' -Raw; ^
     $t -replace '__PERCENT__', $p | Set-Content 'docs\line-coverage.svg' -NoNewline"
  if errorlevel 1 echo Warning: could not refresh docs\line-coverage.svg from template.
)

echo.
echo Output: coverage.cobertura.xml
if exist coveragereport\index.html echo HTML:  coveragereport\index.html
if exist docs\line-coverage.svg echo Badge: docs\line-coverage.svg
popd
exit /b 0

:fail
echo coverage.bat: failed.
popd
exit /b 1
