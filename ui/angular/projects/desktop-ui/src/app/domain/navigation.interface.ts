export interface NavItem {
  id: string;
  label: string;
  icon: string;
  route?: string;
  action?: string;
  disabled?: boolean;
  badge?: string;
  indicator?: string | null;
  activity?: boolean;
}

export interface NavSection {
  items: NavItem[];
  showAtBottom?: boolean;
}

export interface NavItemGroup {
  label?: string;
  items: NavItem[];
}
