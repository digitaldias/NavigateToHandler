# NavigateToHandler
VSIX Extension for navigating to a public handler for the variable under the cursor

This initial version solves a very specific issue in that Visual Studio does not offer a way to navigate from a `Mediator.Send(something)` call to it's corresponding `Handle(something)` method.
The aim of this repository, however, is to reach a state where you can navigate to any public API that utilizes the type of variable under the cursor, not only Mediator Handlers. 

## Usage
To use, simply place your cursor on top of the variable/declaration that you want to look up handlers for. Then, select **EDIT --> Navigate to Handler**, and the extension will open it's corresponding handler class.

> **TIP** <br />
> It is advised to provide a keyboard shortcut to the command. I've selected **CTRL+ALT+H** for mine. 

If there is more than one match, an **Output Window** named `Public Handler Results` will display each match, providing you with the ability to doubleclik to navigate to that handler. 

## Help needed
If anyone knows how I can add some colors to the output window, I'm all open for suggestions!
