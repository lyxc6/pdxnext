namespace PDX.SDK.Contracts.Credential;

public class EmailAndPasswordCredential : ICredential
{
	public string Email { get; set; }

	public string Password { get; set; }

	public EmailAndPasswordCredential(string email, string password)
	{
		Email = email;
		Password = password;
	}
}
