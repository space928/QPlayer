---
title: OSC Cue
---

:::note
This cue type is provided by the OSC Cue plugin which needs to be installed for this cue to 
exist. The OSC Cue plugin is included, preinstalled, with the public releases of QPlayer.
:::

This cue sends an OSC message to external devices when fired. For this cue to work correctly, 
ensure that your OSC settings are correctly configured in the 
[Project Setup](../../project-setup).

### Command
This specifies the OSC command to send when this cue is fired. The OSC message consists of
two parts: the *address* and the *arguments*. Here is an example of an OSC command:
```
	/foo/bar,1,"baz",3.14,false
	↓        | |     |    |
	Address  ↓ |     |    |
			 Arg1    |    |
			   ↓     |    |
			   Arg2  ↓    |
			         Arg3 ↓
					      Arg4
```

The address is a string of characters starting with a forward slash `/`. An address can 
have multiple parts, each separated by slashes `/`. In the above example the address would 
be `/foo/bar`, consisting of two parts: `foo` and `bar`.

The arguments are specified directly after the address and are each separated by a comma `,`.
These arguments can be of any of the following types:

 - **Integer** -- Any whole number (eg: 1,2,3,4)
 - **Float** -- Any decimal number (eg: 3.14)
 - **String** -- Any string of characters enclosed in speech marks (eg: "Hello World")
 - **Boolean** -- A true or false value (eg: true)

