namespace IconChest.Core.Tags;

public class TagValidationException : Exception
{
    public TagValidationException(string message) : base(message) { }
}

public class TagConflictException : Exception
{
    public TagConflictException(string message) : base(message) { }
}

