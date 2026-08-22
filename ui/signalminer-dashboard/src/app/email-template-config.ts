import { EmailStrings } from './email-strings';

export const ZEXTRI_EMAIL_CONFIG = {
  logoUrl: EmailStrings.urls.logo,
  senderEmail: EmailStrings.sender.email,
  senderDisplayName: EmailStrings.sender.displayName,
  senderName: EmailStrings.sender.name,
  senderTitle: EmailStrings.sender.title,
  businessInfo: `${EmailStrings.brand.name}, ${EmailStrings.sender.email}, ${EmailStrings.urls.website}`,
  unsubscribeUrl: EmailStrings.urls.unsubscribe,
  websiteUrl: EmailStrings.urls.website,
  demoUrl: EmailStrings.urls.demo,
  relationshipDemoUrl: EmailStrings.urls.relationshipDemo,
  followUpDemoUrl: EmailStrings.urls.followUpDemo,
  chromeUrl: EmailStrings.urls.chrome,
  edgeUrl: EmailStrings.urls.edge,
  chromeIconUrl: 'https://www.google.com/chrome/static/images/chrome-logo-m100.svg',
  edgeIconUrl: 'https://upload.wikimedia.org/wikipedia/commons/9/98/Microsoft_Edge_logo_%282019%29.svg',
  templateManifestUrl: 'assets/email-templates/manifest.json'
} as const;

export const EMAIL_TEMPLATE_ASSET_PATHS = {
  sharedHeader: 'assets/email-templates/shared/email-header.html',
  sharedSignature: 'assets/email-templates/shared/email-signature.html',
  sharedFooter: 'assets/email-templates/shared/email-footer.html',
  sharedLayout: 'assets/email-templates/shared/email-layout.html'
} as const;

export const FALLBACK_EMAIL_LAYOUT_HTML = [
  '<table data-zextri-email-layout="true" role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;background:#f8fbff;">',
  '<tr>',
  '<td align="center" style="padding:28px 12px;">',
  '<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:640px;border-collapse:collapse;background:#ffffff;border:1px solid #e5e7eb;border-radius:10px;">',
  '<tr>',
  '<td style="padding:30px 28px;font-family:Inter,Arial,Helvetica,sans-serif;color:#111827;">',
  '{{emailContent}}',
  '</td>',
  '</tr>',
  '</table>',
  '</td>',
  '</tr>',
  '</table>'
].join('');

export const SIGNATURE_TEXT_MARKERS = [
  ...EmailStrings.legacyDetection.signatureTextMarkers
] as const;

export const LEGACY_ZEXTRI_SIGNATURE_MARKERS = [
  ...EmailStrings.legacyDetection.zextriSignatureMarkers
] as const;

export const DEFAULT_EMAIL_TEMPLATE = {
  name: EmailStrings.templates.quickIntroduction.name,
  version: '1.0.0',
  subject: EmailStrings.templates.quickIntroduction.defaultSubject,
  fallbackOpening: EmailStrings.templates.quickIntroduction.fallbackOpening,
  fallbackBodyRest: EmailStrings.templates.quickIntroduction.fallbackBodyRest,
  body: [
    `${EmailStrings.templates.shared.greeting} {{firstName}},`,
    '',
    EmailStrings.templates.quickIntroduction.opening,
    '',
    EmailStrings.templates.quickIntroduction.body,
    '',
    EmailStrings.templates.quickIntroduction.plainTextQuestion
  ].join('\n'),
  html: [
    '<p style="margin:0 0 16px;font-size:15px;line-height:24px;color:#111827;">{{emailGreeting}} {{firstName}},</p>',
    '<p style="margin:0 0 16px;font-size:15px;line-height:24px;color:#334155;">{{quickIntroductionOpening}}</p>',
    '<p style="margin:0 0 18px;font-size:15px;line-height:24px;color:#334155;">{{quickIntroductionBody}}</p>',
    '<p style="margin:0 0 22px;font-size:15px;line-height:24px;color:#334155;">{{quickIntroductionQuestion}}</p>',
    '<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 24px;border-collapse:collapse;">',
    '<tr>',
    '<td style="padding:0;font-family:Inter,Arial,Helvetica,sans-serif;">',
    '<table role="presentation" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin:0 0 10px;">',
    '<tr><td bgcolor="#07070a" style="border-radius:10px;padding:12px 18px;"><a href="{{quickIntroductionDemoUrl}}" style="font-family:Inter,Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:800;color:#ffffff;text-decoration:none;"><span style="color:#ffffff;text-decoration:none;">{{ctaWatchDemo}}</span></a></td></tr>',
    '</table>',
    '<table role="presentation" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">',
    '<tr>',
    '<td bgcolor="#ffffff" style="border:1px solid #d9dee7;border-radius:9px;padding:10px 18px;"><a href="{{chromeUrl}}" style="font-family:Inter,Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:800;color:#202124;text-decoration:none;"><img src="{{chromeIconUrl}}" width="18" height="18" alt="" style="display:inline-block;width:18px;height:18px;vertical-align:-4px;margin-right:10px;border:0;"><span style="color:#202124;text-decoration:none;">{{ctaChrome}}</span></a></td>',
    '<td style="width:8px;font-size:8px;line-height:8px;">&nbsp;</td>',
    '<td bgcolor="#1473c7" style="border:1px solid #1473c7;border-radius:9px;padding:10px 18px;"><a href="{{edgeUrl}}" style="font-family:Inter,Arial,Helvetica,sans-serif;font-size:14px;line-height:18px;font-weight:800;color:#ffffff;text-decoration:none;"><img src="{{edgeIconUrl}}" width="18" height="18" alt="" style="display:inline-block;width:18px;height:18px;vertical-align:-4px;margin-right:10px;border:0;"><span style="color:#ffffff;text-decoration:none;">{{ctaEdge}}</span></a></td>',
    '</tr>',
    '</table>',
    '<div style="margin-top:10px;font-size:12px;line-height:18px;color:#64748b;">{{storeAvailability}}</div>',
    '</td>',
    '</tr>',
    '</table>'
  ].join('')
} as const;

export const FALLBACK_ZEXTRI_SIGNATURE_HTML = [
  '<table data-signalminer-signature="true" role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px;border-collapse:collapse;font-family:Arial,Helvetica,sans-serif;color:#111827;">',
  '<tr><td colspan="2" style="padding:0 0 14px;"><div style="width:72px;height:2px;background:#0f766e;line-height:2px;font-size:2px;">&nbsp;</div></td></tr>',
  '<tr>',
  '<td style="width:56px;vertical-align:top;padding:0 14px 0 0;">',
  `<img src="${ZEXTRI_EMAIL_CONFIG.logoUrl}" alt="Zextri" width="44" height="44" style="display:block;width:44px;height:44px;border-radius:10px;border:1px solid #dbe3ea;">`,
  '</td>',
  '<td style="vertical-align:top;padding:0 0 0 14px;border-left:1px solid #d8dee6;">',
  `<div style="font-size:14px;line-height:20px;color:#374151;margin:0 0 8px;">${EmailStrings.templates.shared.signatureClosing}</div>`,
  `<div style="font-size:15px;line-height:21px;font-weight:700;color:#111827;margin:0;">${EmailStrings.sender.name}</div>`,
  `<div style="font-size:13px;line-height:19px;color:#4b5563;margin:2px 0 8px;">${EmailStrings.sender.title}</div>`,
  '<div style="font-size:13px;line-height:19px;color:#374151;margin:0;">',
  `<a href="${EmailStrings.urls.website}" style="color:#0f766e;text-decoration:none;font-weight:700;"><span style="color:#0f766e;text-decoration:none;">${EmailStrings.templates.shared.signatureWebsiteLabel}</span></a>`,
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
    EmailStrings.confirmation.sendTestTitle,
    '',
    `${EmailStrings.confirmation.lead} ${input.leadName}`,
    `${EmailStrings.confirmation.to} ${input.recipient}`,
    `${EmailStrings.confirmation.from} ${input.sender}`,
    `${EmailStrings.confirmation.template} ${input.templateName} v${input.templateVersion}`,
    `${EmailStrings.confirmation.subject} ${input.subject}`,
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
    EmailStrings.confirmation.sendTitle,
    '',
    `${EmailStrings.confirmation.lead} ${input.leadSummary}`,
    `${EmailStrings.confirmation.to} ${input.recipient}`,
    `${EmailStrings.confirmation.from} ${input.sender}`,
    `${EmailStrings.confirmation.template} ${input.templateName} v${input.templateVersion}`,
    `${EmailStrings.confirmation.subject} ${input.subject}`,
    '',
    input.bodyText
  ].join('\n');
}
