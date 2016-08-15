# HabBit(C# 6)
Application for modifying Habbo Hotel's AS3 flash client.

## Requirements
* .NET Framework 4.5

## Arguments
* [Required] `-g <path>` - The game client file path to modify.
* [Optional] `-o <path>` - The output path for the re-assembled game client, and other resources.
* [Optional] `-c <none|zlib|lzma>` - Override compression type to use after the game client has been assembled.

* [Optional] `--dhead` - Dump all Outgoing/Incoming message headers.
* [Optional] `--rev <revision>` - Overrides the revision value found in the client's Outgoing[4000] message class.
* [Optional] `--patt <pattern1> <pattern...>` - Replaces the regex patterns found in the main Habbo class that validate where the client is being hosted from.
* [Optional] `--genrsa <keySize>` - Creates a fresh batch of RSA keys(with private key), this flag will override the `--rsa` argument.
* [Optional] `--rsa <exponent> <modulus>` - Overrides the client's public RSA keys with the ones provided. The provided keys should be in base-16(Hexadecimal).

## Usages
Generate 1024-bit RSA keys, replace revision, and override regex host check patterns.

`-g "C:\Habbo.swf" --genrsa 1024 --rev "NEWPRODUCTION" --patt "(.*)" "[a-z]{3}"`

## Default RSA Keys
```
E:3

N:86851dd364d5c5cece3c883171cc6ddc5760779b992482bd1e20dd296888df91b33b936a7b93f06d29e8870f703a216257dec7c81de0058fea4cc5116f75e6efc4e9113513e45357dc3fd43d4efab5963ef178b78bd61e81a14c603b24c8bcce0a12230b320045498edc29282ff0603bc7b7dae8fc1b05b52b2f301a9dc783b7

D:59ae13e243392e89ded305764bdd9e92e4eafa67bb6dac7e1415e8c645b0950bccd26246fd0d4af37145af5fa026c0ec3a94853013eaae5ff1888360f4f9449ee023762ec195dff3f30ca0b08b8c947e3859877b5d7dced5c8715c58b53740b84e11fbc71349a27c31745fcefeeea57cff291099205e230e0c7c27e8e1c0512b
```
