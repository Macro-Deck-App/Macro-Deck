# Contributing Languages 

Macro Deck 2 uses XML files for translation.

### If your language is missing or incomplete, please consider helping me out by creating a pull request with the translated file!

## Adding/editing a translation file
- Fork this GitHub repository
- Add/edit the translation files
- Create a pull request of the forked repository

## Note
Please use the ISO names for the file name and for the __Language__ node in the file. For __LanguageCode__ use the ISO-639-1 code. You can find more information here: https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes

Here is an example of the first few lines of the file:
```xml
<?xml version="1.0"?>
<Strings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <__Language__>German</__Language__> <!-- The ISO name of the translation -->
  <__LanguageCode__>de</__LanguageCode__> <!-- The ISO639-1 code of the translation -->
  <__Author__>Macro Deck</__Author__> <!-- Your name -->
  <__Version__>2.0.15</__Version__> <!-- The Macro Deck version, this file is based of -->
  .
  .
  .
</Strings>
```

