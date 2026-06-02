interface Env {
  API_ORIGIN: string;
}

export const onRequest: PagesFunction<Env> = async ({ request, env, params }) => {
  const segments = (params.path as string[]) ?? [];
  const url = new URL(request.url);
  const target = `${env.API_ORIGIN}/api/${segments.join('/')}${url.search}`;
  return fetch(target, request);
};
