import { Auth0Client } from "@auth0/nextjs-auth0/server";

export const auth0 = new Auth0Client({
  domain: 'dev-p6v7djlwowndrygd.us.auth0.com',
  clientId: 'p7kXXDxkV1t5f6BApgvavPJfAtbrAECz',
  authorizationParameters: {
    audience: 'https://api.woopulse.local',
    scope: "openid profile email"
  }
});