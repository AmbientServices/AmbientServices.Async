# Overview
AmbientServices.Async is a .NET library that provides tools for migrating even the largest, most challenging and performance-critical projects from non async/TPL code to modern .NET core async/await.

## AA
The static AA (AsyncAwait) class provides a way to make code async-ready little by little rather than the usual "forklift" update normally required due to the zombie virus nature of async.
This has been successfully used to slowly transition a 100K line production web server with hundreds of thousands of monthly users to async over a period of a year with only minor issues due to occasional mistakes in the conversion process.

## HighPerformanceFifoTaskScheduler
HighPerformanceFifoTaskScheduler is a high performance async task scheduler that is highly scalable and far more responsive than the standard .NET ThreadPool.

## Getting Started
In Visual Studio, use Manage Nuget Packages and search nuget.org for AmbientServices to add a package reference for this library.

For .NET Core environments, use:
`dotnet add package https://www.nuget.org/packages/AmbientServices.Async/`


## Miscellaneous
Some provided extension methods may conflict with existing extension methods, so those are put into the separate AmbientServices.Async.Extensions namespace so that they may be included only where needed.

# Library Information

## Author and License
AmbientServices is written and maintained by James Ivie.

AmbientServices is licensed under [MIT](https://opensource.org/licenses/MIT).

## Language and Tools
AmbientServices is written in C#, using .NET Standard 2.0, .NET Core 3.1, .NET 5.0, and .NET 6.0.  Unit tests are written in .NET 6.0.

The code can be built using either Microsoft Visual Studio 2022+, Microsoft Visual Studio Code, or .NET Core command-line utilities.

Binaries are available at https://www.nuget.org/packages/AmbientServices.Async.

## Contributions
Contributions are welcome under the following conditions:
1. enhancements are consistent with the overall scope of the project
2. no new assembly dependencies are introduced
3. code coverage by unit tests cover all new lines and conditions whenever possible
4. documentation (both inline and here) is updated appropriately
5. style for code and documentation contributions remains consistent

## Status
[![.NET](https://github.com/AmbientServices.Async/AmbientServices.Async/actions/workflows/dotnet.yml/badge.svg)](https://github.com/AmbientServices.Async/AmbientServices.Async/actions/workflows/dotnet.yml)