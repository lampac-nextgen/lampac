namespace LogUserRequest.Models.DTO;

public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Fail(string error) => new() { Success = false, Error = error };
}
