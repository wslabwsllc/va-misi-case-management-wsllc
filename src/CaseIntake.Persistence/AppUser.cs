using Microsoft.AspNetCore.Identity;

namespace CaseIntake.Persistence;

/// <summary>Identity user. Owned by Persistence so AppDbContext does not depend on the Identity feature project; E3 extends behavior.</summary>
public class AppUser : IdentityUser;
