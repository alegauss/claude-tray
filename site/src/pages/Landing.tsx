import { Nav } from "../components/Nav";
import { Footer } from "../components/Footer";
import { Hero } from "../components/sections/Hero";
import { Why } from "../components/sections/Why";
import { Tooltip } from "../components/sections/Tooltip";
import { Projection } from "../components/sections/Projection";
import { Statistics } from "../components/sections/Statistics";
import { Notifications } from "../components/sections/Notifications";
import { Insights } from "../components/sections/Insights";
import { Context } from "../components/sections/Context";
import { Profiles } from "../components/sections/Profiles";
import { FeatureIndex } from "../components/sections/FeatureIndex";
import { Privacy } from "../components/sections/Privacy";
import { Menu } from "../components/sections/Menu";
import { Install } from "../components/sections/Install";

// The landing page. The section order is the argument, not a feature list: what the icon is
// → what one hover says → what it predicts → the charts behind the prediction → what it
// interrupts you for → where the tokens went → what a session costs before you type → whose
// account you are looking at → the depth pages → what it reads and sends → the menu that
// drives it → the install.
//
// Privacy sits immediately before the install rather than in a footnote: it is the last
// question a reader has before running something that watches their usage, and answering it
// after the download button would be answering it too late.
export function Landing() {
  return (
    <>
      <Nav />
      <Hero />
      <Why />
      <Tooltip />
      <Projection />
      <Statistics />
      <Notifications />
      <Insights />
      <Context />
      <Profiles />
      <FeatureIndex />
      <Privacy />
      <Menu />
      <Install />
      <Footer />
    </>
  );
}
