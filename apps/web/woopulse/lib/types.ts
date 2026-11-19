// Types shared by several components (DEVLOG #015).
// They mirror the JSON the .NET API returns (camelCase versions of the C# DTOs).

export type Role = "Owner" | "Admin" | "Member";

/** Dtos/StoreDtos.cs → StoreDto */
export type Store = {
  id: number;
  name: string;
  ownerSub: string;
  createdAtUtc: string;
  createdBySub: string;
  updatedAtUtc: string | null;
  updatedBySub: string | null;
  isDeleted: boolean;
  deletedAtUtc: string | null;
  deletedBySub: string | null;
  myRole: Role;
};

/** app/api/profile/route.ts */
export type AuthState = {
  loggedIn: boolean;
  name?: string | null;
  email?: string | null;
  sub?: string | null;
};
