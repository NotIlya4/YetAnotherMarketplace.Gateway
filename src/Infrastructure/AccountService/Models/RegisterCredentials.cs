﻿namespace Infrastructure.AccountService.Models;

public class RegisterCredentials
{
    public string Username { get; }
    public string Email { get; }
    public string Password { get; }

    public RegisterCredentials(string username, string email, string password)
    {
        Username = username;
        Email = email;
        Password = password;
    }
}