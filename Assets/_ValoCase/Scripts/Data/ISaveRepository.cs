namespace ValoCase.Save
{
    public interface ISaveRepository
    {
        bool Exists();
        bool TryLoad(out SaveDataRoot data);
        void Save(SaveDataRoot data);
        void Delete();
    }
}
