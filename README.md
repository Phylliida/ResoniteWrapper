# ResoniteEasyFunctionWrapper

This is a mod that lets you create custom protoflux nodes that call c# code,
while remaining compatible with people without the mod.

## Installation

Follow the instructions for installing [Hot Reload Lib](https://github.com/Nytra/ResoniteHotReloadLib/tree/main?tab=readme-ov-file#pre-requisites),
including installing ResoniteModLoader.

Then move ResoniteEasyFunctionWrapper.dll into your rml_mods folder.

## Usage

There is an [example mod](https://github.com/Phylliida/ResoniteEasyFunctionWrapperExampleMod) with a full example, including hot reloading support.

How it works is you define a class with methods:

```c#
// Name can be anything
public class MyExportedMethods
{
	// All methods should be static
	// Each method will be exported
	// One limitation: all methods must have unique names (no overloading)
	public static int ExampleMethod(int a, int b)
	{
		return a + b;
	}
}
```

Import ResoniteEasyFunctionWrapper

```c#
using ResoniteEasyFunctionWrapper;
```

Now in your `OnEngineInit` you call the wrapper function

```c#
ResoniteEasyFunctionWrapper.ResoniteEasyFunctionWrapper.WrapClass(
    typeof(MyExportedMethods),
    modNamespace: myModNamespace);
```        

And you are done!

The modNamespace can be any combination of ascii characters and `.`.

This method will automatically fetch all the public, static methods in your class.

To use the wrapper, open Resonite, pull up a dev tool, and press "Create new".

The menu will have an additional option "Generate Wrapper Flux".

![Generate Wrapper Flux](https://github.com/Phylliida/ResoniteEasyFunctionWrapper/blob/main/Assets/menu.jpg?raw=true)

Click on that, and displayed will be all the functions you exported.

Click on one of them to get the wrapper flux:

![Autogenerated flux](https://raw.githubusercontent.com/Phylliida/ResoniteEasyFunctionWrapper/main/Assets/Wrapper%20Flux.png)

This is flux that can be used in my custom protoflax node creator tool to call your exported function.

(my public folder is

```
resrec:///U-TessaCoil/R-0cb7a535-e4da-4e93-9725-402f6f3a322e
```
)

For anyone without your mod, the node will do nothing. However, with the mod, your session is still compatible with people without the mod.

The code will only run if you are the one that sent the impulse (LocalUser == you).

## How it works?

Reflection is used to determine the inputs and outputs of the function.

ref and out params are supported and work how you'd expect (ref are inputs and outputs, out are named outputs).

If you return a tuple, each element of the tuple will be turned into an output parameter.

To call your mod function without making any new nodes, we override the WebsocketTextMessageSender node.

If it detects an input WebsocketClient with Url that looks like (for example):

```
mod://example.me.mymod/ExampleMethod/ExampleMethod
```
it will lookup the corresponding function, and call it. To pass in the data, this wrapper flux will write to a dynvar space, and read them back out to locals once the method is done.

This does not mean arbitrary code execution! Only methods that have been passed into ResoniteEasyFunctionWrapper.WrapClass by a mod, can be called.

(if the URL does not look like that, WebsocketTextMessageSender will behave as normal)

## Implementation Details

Methods are supported with parameters that cannot be regularly passed around in flux (like List, Dictionary, etc.). 

To do this, we have a lookup table of objects to uuids, and in resonite the uuids are passed around instead (as strings).

To avoid the same object creating uuids every time it is called, there is a small cache of uuids for each type that is checked first.

Async methods are supported as of v1.0.3!

## Limitations

Do not make two methods named the same thing.