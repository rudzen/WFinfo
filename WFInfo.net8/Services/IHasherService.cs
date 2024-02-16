namespace WFInfo.Services;

public interface IHasherService
{
    string GetMD5hash(string filePath);
}