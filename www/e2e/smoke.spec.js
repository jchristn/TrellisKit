import { expect, test } from '@playwright/test';

async function noHorizontalScroll(page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(0);
}

// The dev server builds the default property (samples/example) unless TK_PROPERTY_DIR is set;
// these checks read names from the page instead of assuming a property.

test('home page loads from the property content without horizontal scroll', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  const brand = await page.locator('.site-header .brand-name').textContent();
  await expect(page).toHaveTitle(new RegExp('^' + brand.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ' \\| '));
  await noHorizontalScroll(page);
});

test('brand files, manifest, sitemap, and robots are generated for the property', async ({ request }) => {
  for (const file of ['/favicon-32.png', '/icon-192.png', '/icon-512.png', '/apple-touch-icon.png', '/brand-mark.png', '/logo.png', '/logo-dark.png', '/og-image.png']) {
    const res = await request.get(file);
    expect(res.status(), file).toBe(200);
    expect(res.headers()['content-type']).toContain('image/png');
  }
  const manifest = await (await request.get('/site.webmanifest')).json();
  expect(manifest.icons).toHaveLength(2);
  expect(await (await request.get('/robots.txt')).text()).toContain('Disallow: /admin');
  expect(await (await request.get('/sitemap.xml')).text()).toContain('<urlset');
});

test('legal and membership pages render without horizontal scroll', async ({ page }) => {
  for (const path of ['/membership', '/privacy']) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await noHorizontalScroll(page);
  }
});

test('sign-in page loads', async ({ page }) => {
  await page.goto('/sign-in');
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  await expect(page.getByLabel('Email')).toBeVisible();
  await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Forgot your password?' })).toBeVisible();
  await noHorizontalScroll(page);
});

test('sign-up page shows the age, terms, and password policy fields', async ({ page }) => {
  await page.goto('/sign-up');
  await expect(page.getByRole('heading', { level: 1, name: 'Create your account' })).toBeVisible();
  await expect(page.getByLabel('Date of birth')).toBeVisible();
  await expect(page.getByText('10 to 128 characters')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Terms of service' }).first()).toBeVisible();
  await noHorizontalScroll(page);
});

test('join link prefills the sign-up code', async ({ page }) => {
  await page.goto('/join?code=seminar2026');
  await expect(page).toHaveURL(/\/sign-up$/);
  await expect(page.getByLabel('Sign-up code (optional)')).toHaveValue('SEMINAR2026');
});

test('membership page redirects to sign-in when signed out', async ({ page }) => {
  await page.goto('/app/membership');
  await expect(page).toHaveURL(/\/sign-in\?next=%2Fapp%2Fmembership$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
});

test('admin login page offers email sign-in and the admin token', async ({ page }) => {
  await page.goto('/admin');
  await expect(page).toHaveURL(/\/admin\/login$/);
  await expect(page.getByLabel('Email')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  await page.getByRole('button', { name: 'Use admin token' }).click();
  await expect(page.getByLabel('Admin token')).toBeVisible();
});
