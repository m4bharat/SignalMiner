import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

type ContactStatus = 'NotContacted' | 'ReadyForManualOutreach' | 'Contacted' | 'Replied' | 'NotInterested' | 'DoNotContact';
type LeadStatus = 'New' | 'Enriched' | 'NeedsManualReview' | 'Qualified' | 'Disqualified' | 'Archived';
type DiscoverySource = 'GitHub' | 'X';
type SourceKind = 'GitHub' | 'Website' | 'X' | 'LinkedInProfileUrlOnly' | 'Manual';
type SourceFilter = '' | 'Imported' | SourceKind;
type DetailTab = 'overview' | 'outreach' | 'email';

const ZEXTRI_LOGO_URL = 'https://zextri.com/icons/zextri-192.png';
const ZEXTRI_EMAIL_SIGNATURE_TEXT = [
  'Best regards,',
  'Zextri Growth Team',
  'AI-powered social intelligence for LinkedIn and X',
  'Write smarter. Follow up better.',
  'Website: https://zextri.com'
].join('\n');

const LEGACY_EMAIL_SIGNATURE = [
  'Best regards,',
  'Zextri Team',
  'https://zextri.com'
].join('\n');

interface Lead {
  id: string;
  displayName: string;
  roleTitle?: string;
  publicEmail?: string;
  websiteUrl?: string;
  gitHubUrl?: string;
  xUrl?: string;
  linkedInUrl?: string;
  fitScore: number;
  scoreRationale: string;
  status: LeadStatus;
  contactStatus: ContactStatus;
  notes?: string;
  company?: { name: string; domain?: string; summary?: string };
  sourceProfiles?: SourceProfile[];
}

interface SourceProfile {
  kind: string;
  url: string;
  publicHandle: string;
}

interface SearchResult {
  items: Lead[];
  total: number;
}

interface LeadImportResult {
  totalRows: number;
  validRows: number;
  importedRows: number;
  skippedRows: number;
  issues: LeadImportIssue[];
  previewRows: LeadImportPreviewRow[];
}

interface LeadImportIssue {
  rowNumber: number;
  field: string;
  message: string;
}

interface LeadImportPreviewRow {
  rowNumber: number;
  displayName: string;
  company?: string;
  publicEmail?: string;
  fitScore: number;
  isDuplicate: boolean;
  duplicateReason?: string;
}

@Component({
  selector: 'sm-root',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './app.component.html'
})
export class AppComponent {
  private readonly http = inject(HttpClient);
  private readonly apiBase = 'http://localhost:5126/api';

  protected readonly leads = signal<Lead[]>([]);
  protected readonly selectedLead = signal<Lead | null>(null);
  protected readonly total = signal(0);
  protected readonly query = signal('');
  protected readonly minFitScore = signal<number | null>(50);
  protected readonly contactStatus = signal<ContactStatus | ''>('');
  protected readonly sourceFilter = signal<SourceFilter>('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly discoveryQuery = signal('founder saas ai');
  protected readonly discoverySource = signal<DiscoverySource>('GitHub');
  protected readonly loading = signal(false);
  protected readonly message = signal('Manual-review-first lead discovery workspace');
  protected readonly outreachNote = signal('');
  protected readonly emailTo = signal('');
  protected readonly emailSubject = signal('');
  protected readonly emailBodyHtml = signal('');
  protected readonly emailAttachments = signal<File[]>([]);
  protected readonly emailReplyTo = signal('');
  protected readonly emailCc = signal('');
  protected readonly emailBcc = signal('');
  protected readonly emailSignatureHtml = signal('');
  protected readonly includeSignature = signal(true);
  protected readonly importFile = signal<File | null>(null);
  protected readonly importResult = signal<LeadImportResult | null>(null);
  protected readonly detailTab = signal<DetailTab>('overview');
  protected readonly detailExpanded = signal(true);

  protected readonly templates = computed(() => {
    const lead = this.selectedLead();
    const name = lead?.displayName.split(' ')[0] ?? 'there';
    return [
      `Hi ${name}, I noticed your work around AI/SaaS and thought Zextri might be relevant. Worth a quick look?`,
      `Hi ${name}, your public GitHub/website signals suggest you may be building workflow software. Zextri helps teams move faster with AI-assisted operations.`,
      `Hi ${name}, I am doing a manual review of potential Zextri fits and your profile stood out. Open to a short note with details?`
    ];
  });

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  protected readonly pageStart = computed(() => {
    if (this.total() === 0) return 0;
    return (this.page() - 1) * this.pageSize() + 1;
  });

  protected readonly pageEnd = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  constructor() {
    this.loadEmailSettings();
    this.search();
  }

  protected search(resetPage = true): void {
    if (resetPage) {
      this.page.set(1);
    }

    this.loading.set(true);
    let params = new HttpParams()
      .set('page', this.page())
      .set('pageSize', this.pageSize());

    if (this.query()) params = params.set('query', this.query());
    if (this.minFitScore() !== null) params = params.set('minFitScore', this.minFitScore()!.toString());
    if (this.contactStatus()) params = params.set('contactStatus', this.contactStatus());
    if (this.sourceFilter() === 'Imported') {
      params = params.set('importedOnly', 'true');
    } else if (this.sourceFilter()) {
      params = params.set('sourceKind', this.sourceFilter());
    }

    this.http.get<SearchResult>(`${this.apiBase}/leads/search`, { params }).subscribe({
      next: result => {
        this.leads.set(result.items);
        this.total.set(result.total);
        const firstLead = result.items[0] ?? null;
        if (firstLead) {
          this.prepareLeadEmail(firstLead);
        } else {
          this.selectedLead.set(null);
          this.emailTo.set('');
        }
        if (this.page() > this.totalPages()) {
          this.page.set(this.totalPages());
          this.search(false);
          return;
        }
        this.loading.set(false);
      },
      error: () => {
        this.message.set('API unavailable. Start SignalMiner.Api on http://localhost:5000.');
        this.loading.set(false);
      }
    });
  }

  protected discover(): void {
    this.loading.set(true);
    this.http.post<Lead[]>(`${this.apiBase}/leads/discover`, {
      query: this.discoveryQuery(),
      limit: 25,
      source: this.discoverySource()
    }).subscribe({
      next: leads => {
        this.message.set(`Discovered ${leads.length} ${this.discoverySource()} leads for manual review.`);
        this.search();
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Discovery failed. Check API, PostgreSQL, and source rate limits.');
        this.loading.set(false);
      }
    });
  }

  protected selectImportFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.importFile.set(file);
    this.importResult.set(null);
    if (file) {
      this.message.set(`Ready to preview ${file.name}.`);
    }
  }

  protected previewImport(): void {
    this.uploadImport(false);
  }

  protected commitImport(): void {
    this.uploadImport(true);
  }

  protected primarySource(lead: Lead): { label: string; url?: string } {
    if (lead.gitHubUrl) return { label: 'GitHub', url: lead.gitHubUrl };
    if (lead.xUrl) return { label: 'X', url: lead.xUrl };
    if (lead.linkedInUrl) return { label: 'LinkedIn', url: lead.linkedInUrl };
    if (lead.websiteUrl) return { label: 'Website', url: lead.websiteUrl };
    return { label: 'None' };
  }

  protected clearFilters(): void {
    this.query.set('');
    this.minFitScore.set(null);
    this.contactStatus.set('');
    this.sourceFilter.set('');
    this.search();
  }

  protected toggleDetailExpanded(): void {
    this.detailExpanded.update(value => !value);
  }

  protected nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update(value => value + 1);
    this.search(false);
  }

  protected previousPage(): void {
    if (this.page() <= 1) return;
    this.page.update(value => value - 1);
    this.search(false);
  }

  protected changePageSize(value: number): void {
    this.pageSize.set(value);
    this.search();
  }

  protected enrich(lead: Lead): void {
    this.loading.set(true);
    this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/enrich`, {}).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        this.message.set('Lead enriched from public website data.');
        this.search();
      },
      error: () => {
        this.message.set('Enrichment failed or no crawlable public website was available.');
        this.loading.set(false);
      }
    });
  }

  protected selectLead(lead: Lead): void {
    this.prepareLeadEmail(lead);
  }

  private prepareLeadEmail(lead: Lead): void {
    this.selectedLead.set(lead);
    this.detailTab.set('overview');
    this.outreachNote.set('');
    this.emailTo.set(lead.publicEmail ?? '');
    this.emailSubject.set(`Quick note from Zextri`);
    this.emailBodyHtml.set(this.withDefaultSignature(this.textToHtml(this.templates()[0] ?? '')));
    this.emailAttachments.set([]);
  }

  protected useTemplate(template: string): void {
    this.emailBodyHtml.set(this.withDefaultSignature(this.textToHtml(template)));
  }

  protected updateEmailBody(event: Event): void {
    this.emailBodyHtml.set((event.target as HTMLElement).innerHTML);
  }

  protected updateEmailSignature(event: Event): void {
    this.emailSignatureHtml.set((event.target as HTMLElement).innerHTML);
    if (this.includeSignature()) {
      this.emailBodyHtml.set(this.withDefaultSignature(this.emailBodyHtml()));
    }
  }

  protected setIncludeSignature(value: boolean): void {
    this.includeSignature.set(value);
    this.emailBodyHtml.set(value
      ? this.withDefaultSignature(this.emailBodyHtml())
      : this.removeSignature(this.emailBodyHtml()));
  }

  protected formatEmail(command: string, value?: string): void {
    document.execCommand(command, false, value);
  }

  protected addEmailLink(): void {
    const url = window.prompt('Link URL');
    if (!url) return;

    const normalizedUrl = url.startsWith('http') ? url : `https://${url}`;
    this.formatEmail('createLink', normalizedUrl);
  }

  protected selectEmailAttachments(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.emailAttachments.set(Array.from(input.files ?? []));
  }

  protected saveEmailSettings(): void {
    const settings = {
      replyTo: this.emailReplyTo().trim(),
      cc: this.emailCc().trim(),
      bcc: this.emailBcc().trim(),
      signatureHtml: this.emailSignatureHtml().trim(),
      includeSignature: this.includeSignature()
    };

    localStorage.setItem('signalminer.emailSettings', JSON.stringify(settings));
    this.message.set('Email settings saved for this browser.');
  }

  protected insertSignature(): void {
    const signatureHtml = this.signatureHtml();
    if (!signatureHtml) {
      this.message.set('Add a signature in email settings first.');
      return;
    }

    this.emailBodyHtml.set(this.appendSignature(this.emailBodyHtml(), signatureHtml));
  }

  protected useZextriSignature(): void {
    this.emailSignatureHtml.set(this.zextriSignatureHtml());
    this.includeSignature.set(true);
    this.emailBodyHtml.set(this.withDefaultSignature(this.removeSignature(this.emailBodyHtml())));
    this.message.set('Market-ready Zextri signature applied.');
  }

  protected sendEmail(): void {
    const lead = this.selectedLead();
    if (!lead) return;

    const toEmail = this.emailTo().trim();
    if (!toEmail) {
      this.message.set('Enter a recipient email address.');
      return;
    }

    this.loading.set(true);
    const form = new FormData();
    const bodyHtml = this.finalEmailBodyHtml();
    form.append('toEmail', toEmail);
    form.append('subject', this.emailSubject());
    form.append('body', this.htmlToText(bodyHtml));
    form.append('bodyHtml', bodyHtml);
    if (this.emailReplyTo().trim()) form.append('replyTo', this.emailReplyTo().trim());
    if (this.emailCc().trim()) form.append('cc', this.emailCc().trim());
    if (this.emailBcc().trim()) form.append('bcc', this.emailBcc().trim());
    for (const file of this.emailAttachments()) {
      form.append('attachments', file, file.name);
    }

    this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/email`, form).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        this.message.set(`Email sent to ${toEmail}.`);
        this.loading.set(false);
        this.search(false);
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Email could not be sent. Check SMTP settings and try again.');
        this.loading.set(false);
      }
    });
  }

  private textToHtml(value: string): string {
    return value
      .split(/\r?\n/)
      .map(line => line.trim())
      .filter(line => line.length > 0)
      .map(line => `<p>${this.escapeHtml(line)}</p>`)
      .join('');
  }

  private htmlToText(value: string): string {
    const container = document.createElement('div');
    container.innerHTML = value;
    return (container.innerText || container.textContent || '').trim();
  }

  private escapeHtml(value: string): string {
    const container = document.createElement('div');
    container.textContent = value;
    return container.innerHTML;
  }

  private loadEmailSettings(): void {
    const saved = localStorage.getItem('signalminer.emailSettings');
    if (!saved) {
      this.emailSignatureHtml.set(this.zextriSignatureHtml());
      return;
    }

    try {
      const settings = JSON.parse(saved) as {
        replyTo?: string;
        cc?: string;
        bcc?: string;
        signatureHtml?: string;
        signature?: string;
        includeSignature?: boolean;
      };
      this.emailReplyTo.set(settings.replyTo ?? '');
      this.emailCc.set(settings.cc ?? '');
      this.emailBcc.set(settings.bcc ?? '');
      this.emailSignatureHtml.set(this.normalizeSavedSignature(settings.signatureHtml, settings.signature));
      this.includeSignature.set(settings.includeSignature ?? true);
    } catch {
      this.message.set('Saved email settings could not be loaded.');
      this.emailSignatureHtml.set(this.zextriSignatureHtml());
    }
  }

  private finalEmailBodyHtml(): string {
    const bodyHtml = this.emailBodyHtml();
    const signatureHtml = this.signatureHtml();
    return this.includeSignature() && signatureHtml
      ? this.appendSignature(bodyHtml, signatureHtml)
      : bodyHtml;
  }

  private signatureHtml(): string {
    return this.emailSignatureHtml().trim();
  }

  private appendSignature(bodyHtml: string, signatureHtml: string): string {
    if (bodyHtml.includes('data-signalminer-signature="true"') || bodyHtml.includes('class="email-signature"')) {
      return bodyHtml;
    }

    return `${bodyHtml}<br>${signatureHtml}`;
  }

  private withDefaultSignature(bodyHtml: string): string {
    const signatureHtml = this.signatureHtml();
    return this.includeSignature() && signatureHtml
      ? this.appendSignature(this.removeSignature(bodyHtml), signatureHtml)
      : this.removeSignature(bodyHtml);
  }

  private removeSignature(bodyHtml: string): string {
    const container = document.createElement('div');
    container.innerHTML = bodyHtml;
    container.querySelectorAll('[data-signalminer-signature="true"], .email-signature').forEach(signature => {
      const previous = signature.previousSibling;
      if (previous?.nodeName === 'BR') {
        previous.remove();
      }
      signature.remove();
    });
    return container.innerHTML.trim();
  }

  private normalizeSavedSignature(signatureHtml?: string, legacySignature?: string): string {
    if (signatureHtml?.trim()) {
      return signatureHtml.trim();
    }

    if (!legacySignature?.trim() ||
        legacySignature === LEGACY_EMAIL_SIGNATURE ||
        legacySignature === ZEXTRI_EMAIL_SIGNATURE_TEXT) {
      return this.zextriSignatureHtml();
    }

    return this.customSignatureHtml(legacySignature);
  }

  private zextriSignatureHtml(): string {
    return [
      '<table data-signalminer-signature="true" role="presentation" cellpadding="0" cellspacing="0" style="margin-top:18px;border-collapse:collapse;font-family:Inter,Segoe UI,Arial,sans-serif;color:#172033;">',
      '<tr>',
      '<td style="width:58px;vertical-align:top;padding:0 14px 0 0;">',
      `<img src="${ZEXTRI_LOGO_URL}" alt="Zextri" width="48" height="48" style="display:block;width:48px;height:48px;border-radius:12px;border:1px solid #e2e8f0;">`,
      '</td>',
      '<td style="vertical-align:top;padding:0 0 0 14px;border-left:3px solid #7c4dff;">',
      '<div style="font-size:14px;line-height:20px;color:#475569;margin:0 0 2px;">Best regards,</div>',
      '<div style="font-size:16px;line-height:22px;font-weight:700;color:#172033;margin:0;">Zextri Growth Team</div>',
      '<div style="font-size:13px;line-height:19px;color:#475569;margin:3px 0 8px;">AI-powered social intelligence for LinkedIn and X</div>',
      '<div style="display:inline-block;font-size:12px;line-height:18px;font-weight:700;color:#1d4ed8;background:#eff6ff;border:1px solid #bfdbfe;border-radius:999px;padding:4px 10px;margin:0 0 8px;">Write smarter. Follow up better.</div>',
      '<div style="font-size:13px;line-height:19px;color:#475569;">',
      '<a href="https://zextri.com" style="color:#1d4ed8;text-decoration:none;font-weight:700;">zextri.com</a>',
      '</div>',
      '</td>',
      '</tr>',
      '</table>'
    ].join('');
  }

  private customSignatureHtml(signature: string): string {
    return [
      '<div data-signalminer-signature="true" style="margin-top:18px;padding:12px 0 0;border-top:1px solid #e2e8f0;font-family:Inter,Segoe UI,Arial,sans-serif;color:#172033;">',
      this.textToHtml(signature),
      '</div>'
    ].join('');
  }

  protected updateStatus(status: ContactStatus): void {
    const lead = this.selectedLead();
    if (!lead) return;

    this.http.patch<Lead>(`${this.apiBase}/leads/${lead.id}/outreach-status`, {
      status,
      note: this.outreachNote() || `Manual status changed to ${status}`
    }).subscribe(updated => {
      this.selectedLead.set(updated);
      this.message.set('Manual outreach status updated.');
      this.search();
    });
  }

  private uploadImport(commit: boolean): void {
    const file = this.importFile();
    if (!file) {
      this.message.set('Choose a CSV or Excel file first.');
      return;
    }

    this.loading.set(true);
    const form = new FormData();
    form.append('file', file);
    form.append('commit', String(commit));

    this.http.post<LeadImportResult>(`${this.apiBase}/leads/import`, form).subscribe({
      next: result => {
        this.importResult.set(result);
        this.message.set(commit
          ? `Imported ${result.importedRows} leads; skipped ${result.skippedRows}.`
          : `Previewed ${result.validRows} valid rows from ${result.totalRows}.`);
        this.loading.set(false);
        if (commit) {
          this.sourceFilter.set('Imported');
          this.search();
        }
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Import failed. Check the file format and required columns.');
        this.loading.set(false);
      }
    });
  }
}
