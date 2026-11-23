using System;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ForumPost
{
	public string Username { get; set; }

	public int UserId { get; set; }

	public string UserTitle { get; set; }

	public string Url { get; set; }

	public int PostId { get; set; }

	public string Message { get; set; }

	public DateTime Created { get; set; }

	public string Avatar { get; set; }

	public bool HasIdenticalAttributes(ForumPost post)
	{
		if (post == null)
		{
			return this == null;
		}
		if (PostId != post.PostId)
		{
			return false;
		}
		if (Url != post.Url)
		{
			return false;
		}
		if (Created != post.Created)
		{
			return false;
		}
		if (Message != post.Message)
		{
			return false;
		}
		if (Username != post.Username)
		{
			return false;
		}
		if (UserId != post.UserId)
		{
			return false;
		}
		if (UserTitle != post.UserTitle)
		{
			return false;
		}
		if (Avatar != post.Avatar)
		{
			return false;
		}
		return true;
	}
}
