# Contributing

## General

Questions and feature suggestions for  `Navigate to Handler` should be posted under the [Discussions](https://github.com/digitaldias/NavigateToHandler/discussions) tab

Issues and bugs should be reported under the [Issues](https://github.com/digitaldias/NavigateToHandler/issues) tab. 

## Pull requests

I humbly ask that you use the pull request template that has been set up and follow that. Place your checks, and delete the lines that do not apply

## Code

### Code style
In general, the code follows [the usual coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) for C#. 
There are some minor exceptions: 

**Avoid `if/else` statements, instead use the tri-state operator, i.e.**

```csharp
var commandResult = await _mediator.Send(command, cancellationToken);
return commandResult.Succeeded
  ? OK(commandResult)
  : Failed(commandResult)
```

### Prefer Git Rebase

Prefer git rebase over pulls. This gives a cleaner changelog to the code
```cmd
myBranch> git checkout main
main> git pull
main> git checkout -
myBranc> git rebase main
```

### Squash commit

When your PR is accepted, please squash merge into main. Include the relevant commit messages.





