export type LegalDocumentId = 'privacy' | 'terms' | 'cookies';

export interface LegalDocument {
  readonly id: LegalDocumentId;
  readonly title: string;
  readonly description: string;
  readonly intro: string;
  /** What the final text must cover. Shown as a checklist while the page is a placeholder. */
  readonly topics: readonly string[];
}

/**
 * Legal pages are PLACEHOLDERS: each lists what the final text must cover.
 * The final texts must be reviewed by a lawyer before go-live (docs/PLAN.md §3.7).
 */
export const LEGAL_PLACEHOLDER_NOTICE = $localize`:@@legal.placeholderNotice:This page is a placeholder. The final text is being prepared and will be reviewed by a lawyer before the service opens.`;

export const LEGAL_TOPICS_HEADING = $localize`:@@legal.topicsHeading:What this document will cover`;

export const LEGAL_DOCUMENTS: Record<LegalDocumentId, LegalDocument> = {
  privacy: {
    id: 'privacy',
    title: $localize`:@@legal.privacy.title:Privacy policy`,
    description: $localize`:@@legal.privacy.description:How Pracownia Bajkowe Cuda collects, uses and protects your personal data.`,
    intro: $localize`:@@legal.privacy.intro:We collect only the data we need to talk to you and to make your order.`,
    topics: [
      $localize`:@@legal.privacy.topic1:Who the data controller is and how to contact them.`,
      $localize`:@@legal.privacy.topic2:Which data we collect (account, chat messages, pictures, orders) and for what purposes.`,
      $localize`:@@legal.privacy.topic3:The legal basis for each purpose.`,
      $localize`:@@legal.privacy.topic4:How long we keep the data, including order records.`,
      $localize`:@@legal.privacy.topic5:Who processes data on our behalf: Microsoft Azure (hosting and email), Google and Meta (sign-in).`,
      $localize`:@@legal.privacy.topic6:Your rights: access, correction, deletion, data export and complaint to a supervisory authority.`,
      $localize`:@@legal.privacy.topic7:How to export your data and delete your account.`,
    ],
  },
  terms: {
    id: 'terms',
    title: $localize`:@@legal.terms.title:Terms of service`,
    description: $localize`:@@legal.terms.description:The rules for ordering a custom-sewn toy from Pracownia Bajkowe Cuda.`,
    intro: $localize`:@@legal.terms.intro:These terms describe how an order is agreed, paid for and delivered.`,
    topics: [
      $localize`:@@legal.terms.topic1:Who provides the service and how to contact us.`,
      $localize`:@@legal.terms.topic2:How an order is made: you describe the toy in the chat and we create the order.`,
      $localize`:@@legal.terms.topic3:Prices in euro and payment by the instructions sent in the chat.`,
      $localize`:@@legal.terms.topic4:Production and delivery times.`,
      $localize`:@@legal.terms.topic5:Withdrawal rules for made-to-order items.`,
      $localize`:@@legal.terms.topic6:Complaints and what happens if something is wrong with the toy.`,
    ],
  },
  cookies: {
    id: 'cookies',
    title: $localize`:@@legal.cookies.title:Cookie information`,
    description: $localize`:@@legal.cookies.description:Which cookies the Pracownia Bajkowe Cuda website uses and why.`,
    intro: $localize`:@@legal.cookies.intro:This site uses only strictly necessary cookies. There are no analytics or advertising cookies.`,
    topics: [
      $localize`:@@legal.cookies.topic1:Sign-in session cookie: keeps you signed in.`,
      $localize`:@@legal.cookies.topic2:Security cookie: protects forms against forged requests.`,
      $localize`:@@legal.cookies.topic3:Language cookie: remembers the language you chose.`,
      $localize`:@@legal.cookies.topic4:How to delete or block cookies in your browser, and what stops working if you do.`,
    ],
  },
};
