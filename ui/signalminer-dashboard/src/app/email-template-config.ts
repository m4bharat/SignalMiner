export const ZEXTRI_EMAIL_CONFIG = {
  logoUrl: 'https://zextri.com/icons/zextri-192.png',
  senderEmail: 'hello@zextri.com',
  senderDisplayName: 'Bharat from Zextri',
  senderName: 'Bharat Bhushan',
  senderTitle: 'Founder, Zextri',
  businessInfo: 'Zextri, hello@zextri.com, https://zextri.com',
  unsubscribeUrl: 'https://zextri.com/unsubscribe',
  websiteUrl: 'https://zextri.com',
  demoUrl: 'https://zextri.com/#/',
  templateManifestUrl: 'assets/email-templates/manifest.json'
} as const;

export const EMAIL_TEMPLATE_ASSET_PATHS = {
  sharedHeader: 'assets/email-templates/shared/email-header.html',
  sharedSignature: 'assets/email-templates/shared/email-signature.html',
  sharedFooter: 'assets/email-templates/shared/email-footer.html',
  sharedLayout: 'assets/email-templates/shared/email-layout.html'
} as const;

export const SIGNATURE_TEXT_MARKERS = [
  'best regards',
  'warm regards',
  'bharat bhushan',
  'zextri growth team',
  'zextri team',
  'website: https://zextri.com',
  'zextri.com'
] as const;

export const LEGACY_ZEXTRI_SIGNATURE_MARKERS = [
  'ai-assisted operations for faster-moving teams',
  'ai-powered social intelligence for linkedin and x',
  'write smarter. follow up better.'
] as const;

export const DEFAULT_EMAIL_TEMPLATE = {
  name: 'Quick Introduction',
  version: '1.0.0',
  subject: 'A quick idea for {{company}}',
  fallbackOpening: 'Hi there,\n\nI thought Zextri could be useful for your growth workflow.',
  fallbackBodyRest: "Zextri helps teams identify relationships that need attention and write timely, context-aware follow-ups across LinkedIn and X.\n\nWould you be open to a quick look? I'd be happy to send a short demo.",
  body: [
    'Hi {{firstName}},',
    '',
    'I came across your work at {{company}} and thought Zextri could be useful for your growth workflow.',
    '',
    'Zextri helps teams identify relationships that need attention and write timely, context-aware follow-ups across LinkedIn and X.',
    '',
    "Would you be open to a quick look? I'd be happy to send a short demo."
  ].join('\n')
} as const;

export const FALLBACK_ZEXTRI_SIGNATURE_HTML = [
  '<table data-signalminer-signature="true" role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px;border-collapse:collapse;font-family:Arial,Helvetica,sans-serif;color:#111827;">',
  '<tr><td colspan="2" style="padding:0 0 14px;"><div style="width:72px;height:2px;background:#0f766e;line-height:2px;font-size:2px;">&nbsp;</div></td></tr>',
  '<tr>',
  '<td style="width:56px;vertical-align:top;padding:0 14px 0 0;">',
  `<img src="${ZEXTRI_EMAIL_CONFIG.logoUrl}" alt="Zextri" width="44" height="44" style="display:block;width:44px;height:44px;border-radius:10px;border:1px solid #dbe3ea;">`,
  '</td>',
  '<td style="vertical-align:top;padding:0 0 0 14px;border-left:1px solid #d8dee6;">',
  '<div style="font-size:14px;line-height:20px;color:#374151;margin:0 0 8px;">Warm regards,</div>',
  '<div style="font-size:15px;line-height:21px;font-weight:700;color:#111827;margin:0;">Bharat Bhushan</div>',
  '<div style="font-size:13px;line-height:19px;color:#4b5563;margin:2px 0 8px;">Founder, Zextri</div>',
  '<div style="font-size:13px;line-height:19px;color:#374151;margin:0;">',
  '<a href="https://zextri.com" style="color:#0f766e;text-decoration:none;font-weight:700;">zextri.com</a>',
  '</div>',
  '</td>',
  '</tr>',
  '</table>'
].join('');

export const CUSTOM_SIGNATURE_WRAPPER = {
  before: '<div data-signalminer-signature="true" style="margin-top:18px;padding:12px 0 0;border-top:1px solid #e2e8f0;font-family:Inter,Segoe UI,Arial,sans-serif;color:#172033;">',
  after: '</div>'
} as const;

export function buildTestEmailConfirmation(input: {
  leadName: string;
  recipient: string;
  sender: string;
  templateName: string;
  templateVersion: string;
  subject: string;
  bodyText: string;
}): string {
  return [
    'Send test email?',
    '',
    `Lead: ${input.leadName}`,
    `To: ${input.recipient}`,
    `From: ${input.sender}`,
    `Template: ${input.templateName} v${input.templateVersion}`,
    `Subject: ${input.subject}`,
    '',
    input.bodyText
  ].join('\n');
}

export function buildSendEmailConfirmation(input: {
  leadSummary: string;
  recipient: string;
  sender: string;
  templateName: string;
  templateVersion: string;
  subject: string;
  bodyText: string;
}): string {
  return [
    'Send email?',
    '',
    `Lead: ${input.leadSummary}`,
    `To: ${input.recipient}`,
    `From: ${input.sender}`,
    `Template: ${input.templateName} v${input.templateVersion}`,
    `Subject: ${input.subject}`,
    '',
    input.bodyText
  ].join('\n');
}
