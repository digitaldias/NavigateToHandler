# NavigateToHandler
VSIX Extension for navigating to a public handler for the variable under the cursor

This initial version solves a particular issue that Visual Studio does not offer: A way to navigate from a `_mediator.Send(something)` call to its corresponding `Handle(something)` method.
The aim of this repository, however, is to reach a state where you can navigate to any public API that utilizes the variable type under the cursor, not only Mediator Handlers. 

## Usage
To use, simply place your cursor on top of the variable/declaration for which you want to look up handlers. Then, select **EDIT --> Navigate to Handler**, and the extension will open its corresponding handler class.

> **TIP** <br />
> It is advised to provide a keyboard shortcut to the command. I've selected **CTRL+ALT+H** for mine. 

If there is more than one match, an **Output Window** named `Public Handler Results` will display each match, allowing you to double-click to navigate to that handler. 

## Testing

The project uses the solution [TestNavigateToHandler](https://github.com/digitaldias/TestNavigateToHandler) for verification.

## Help needed
I am still working on generic type support, as it is not functioning properly. Any help provided is greatly appreciated
