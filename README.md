# NavigateToHandler
VSIX Extension for navigating to a MediatR handler

This initial version solves a very specific issue in that Visual Studio does not offer a way to navigate from a `Mediator.Send(something)` call to it's corresponding `Handle(something)` method.
The aim of this repository, however, is to reach a state where you can navigate to any public API that utilizes the type of variable under the cursor. 

## Usage
To use, simply place your cursor on top of the variable that you need to look up the handler for, and the extension will open it's corresponding handler class. 
