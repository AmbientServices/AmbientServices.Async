rd .vs /s /q
for %%p in (AmbientServices.Async AmbientServices.Async.Samples AmbientServices.Async.Test) do for %%f in (obj bin\Debug bin\Release) do rd %%p\%%f /s /q
for %%p in (AmbientServices.Async AmbientServices.Async.Samples AmbientServices.Async.Test) do del %%p\packages.config /q
rd packages /s /q
rd TestResults /s /q
for %%p in (AmbientServices.Async AmbientServices.Async.Samples AmbientServices.Async.Test) do rd %%p\TestResults /s /q

