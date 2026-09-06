# QPlayer.Tests

This project contains a few automated tests for QPlayer. These tests serve mostly to protect 
QPlayer's more complicated logic paths from regressions. As such these tests work more as 
end-to-end tests than discreet unit tests, as it's easier to cover a large surface area of 
the code. 

## Running
These tests can be run by building the project and going to the Visual Studio 'Test Explorer' 
and running the desired tests. Alternatively, the dotnet cli can be used:

```sh
dotnet test
```

The UI tests spawn a new instance of QPlayer, forcefuly closing any other open instances of 
QPlayer in the process. As these tests rely on UI Automation, which can be a bit flaky, it's 
recommended to not interact with the window under test for the duration of the tests, this 
includes moving and resizing the window.

