# AutoWaybackMachine
AutoWaybackMachine is an automation tool for backing up urls on WaybackMachine.

# Usage

### **Browser**
You need to provide a browser (*Choices: `Chromium`, `Chrome`, `Firefox`*)
```
autowbm -b chromium
```

### **Login File**

If the login file is a `.txt` file, it should be formatted as follows:
```
email
password
```
If the login file is a `.json` file, it should be formatted as follows:
```
{
    "email": "example@example.com",
    "password": "verysecurepassword123!!$#"
}
```

### **Backing up urls from command line**
Backing up 1 url
```
./autowbm -l login.txt -u https://example.com
```
Backing up 3 urls
```
autowbm -l login.txt -u https://example.com -u https://example2.com -u https://example3.com
```

### **Backing up urls from a file**
Each line in the file should be a url
```
autowbm -l login.txt -up urls.txt
```

# How to run the program?
You can run the program with dotnet run -- <arguments>, compile an executable file using [dotnet publish](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) or download a compiled verion from [Releases](https://github.com/DontEatOreo/AutoWaybackMachine/releases) Tab

# NuGet Packages
```
Selenium.WebDriver
System.CommandLine
```